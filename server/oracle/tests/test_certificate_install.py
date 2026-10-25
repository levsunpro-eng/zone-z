"""Local tests with dummy bytes and mocked OS/crypto calls; no keys are made."""
import hashlib
import importlib.util
import os
from pathlib import Path
import ssl
import subprocess
import tempfile
import unittest
from unittest.mock import Mock, call, patch

SOURCE = Path(__file__).absolute().parents[1] / "install-certificate.py"
SPEC = importlib.util.spec_from_file_location("zonez_certificate_install", SOURCE)
HOOK = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HOOK)


class PairValidationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.lineage = Path(self.temporary.name)
        (self.lineage / "fullchain.pem").write_bytes(b"DUMMY PUBLIC CERTIFICATE")
        (self.lineage / "privkey.pem").write_bytes(b"DUMMY TEST BYTES, NOT A KEY")

    def test_verifies_key_match_ip_and_minimum_lifetime(self):
        with patch.object(HOOK.ssl, "SSLContext") as context, patch.object(HOOK.subprocess, "run") as run:
            run.return_value = subprocess.CompletedProcess([], 0)
            HOOK.validate_pair(self.lineage, "198.51.100.10")
            context.return_value.load_cert_chain.assert_called_once_with(
                str(self.lineage / "fullchain.pem"), str(self.lineage / "privkey.pem"))
            self.assertEqual(run.call_args_list[0].args[0][-2:], ["-checkend", "86400"])
            self.assertEqual(run.call_args_list[1].args[0][-2:], ["-checkip", "198.51.100.10"])

    def test_rejects_invalid_pair_before_publication(self):
        with patch.object(HOOK.ssl, "SSLContext") as context, patch.object(HOOK.subprocess, "run") as run:
            context.return_value.load_cert_chain.side_effect = ssl.SSLError("key mismatch")
            with self.assertRaises(ssl.SSLError):
                HOOK.validate_pair(self.lineage, "198.51.100.10")
            run.assert_not_called()

    def test_rejects_wrong_ip_or_expiring_certificate(self):
        for results in ((1,), (0, 1)):
            with self.subTest(results=results), patch.object(HOOK.ssl, "SSLContext"), patch.object(HOOK.subprocess, "run") as run:
                run.side_effect = [subprocess.CompletedProcess([], value) for value in results]
                with self.assertRaises(ValueError):
                    HOOK.validate_pair(self.lineage, "198.51.100.10")

    def test_rejects_excessive_file_sizes(self):
        (self.lineage / "privkey.pem").write_bytes(b"X" * 32769)
        with patch.object(HOOK.ssl, "SSLContext") as context:
            with self.assertRaises(ValueError):
                HOOK.validate_pair(self.lineage, "198.51.100.10")
            context.assert_not_called()


class CertificatePublishTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.lineage = self.root / "lineage"
        self.lineage.mkdir()
        self.tls = self.root / "tls"
        self.tls.mkdir()
        self.current = self.tls / "current"
        self.certificate = b"DUMMY CERTIFICATE V1"
        self.key = b"DUMMY TEST BYTES, NOT A PRIVATE KEY"
        (self.lineage / "fullchain.pem").write_bytes(self.certificate)
        (self.lineage / "privkey.pem").write_bytes(self.key)
        self.pointer = None
        self.publications = []
        original_exists = Path.exists
        original_symlink = Path.is_symlink
        original_readlink = os.readlink

        def exists(path):
            return bool(self.pointer) or original_exists(path) if path == self.current else original_exists(path)

        def is_symlink(path):
            return bool(self.pointer) if path == self.current else original_symlink(path)

        def readlink(path):
            return self.pointer if Path(path) == self.current else original_readlink(path)

        def publish(root, target):
            self.assertEqual(root, self.tls)
            # Both files must already be installed before the atomic switch.
            self.assertTrue((root / target / "fullchain.pem").is_file())
            self.assertTrue((root / target / "privkey.pem").is_file())
            self.pointer = target
            self.publications.append(target)

        self.patchers = [patch.object(Path, "exists", exists), patch.object(Path, "is_symlink", is_symlink),
                         patch.object(HOOK.os, "readlink", readlink), patch.object(HOOK, "point_current", publish),
                         patch.object(HOOK, "validate_pair"), patch.object(HOOK, "secure"),
                         patch.object(HOOK.subprocess, "run", return_value=subprocess.CompletedProcess([], 0))]
        self.mocks = [patcher.start() for patcher in self.patchers]
        for patcher in reversed(self.patchers):
            self.addCleanup(patcher.stop)
        self.validation, self.permissions, self.run = self.mocks[-3:]

    def install(self):
        return HOOK.install_certificate(self.lineage, self.tls, "198.51.100.10", 123)

    def test_copies_pair_protects_files_and_reloads_once(self):
        self.assertTrue(self.install())
        bundle = self.tls / self.pointer
        self.assertEqual((bundle / "fullchain.pem").read_bytes(), self.certificate)
        self.assertEqual((bundle / "privkey.pem").read_bytes(), self.key)
        self.assertEqual([item.args[1] for item in self.permissions.call_args_list], [0o640, 0o640, 0o750])
        self.assertEqual(self.validation.call_count, 2, "Validate source and copied immutable pair")
        self.assertEqual(self.run.call_args_list[-1].args[0], ["/bin/systemctl", "reload", "zonez-lobby.service"])

    def test_same_certificate_is_idempotent(self):
        self.assertTrue(self.install())
        self.run.reset_mock()
        self.assertFalse(self.install())
        self.run.assert_not_called()
        self.assertEqual(len(self.publications), 1)

    def test_failed_validation_keeps_old_pointer(self):
        self.install()
        previous = self.pointer
        self.validation.side_effect = ValueError("bad certificate")
        with self.assertRaises(ValueError):
            self.install()
        self.assertEqual(self.pointer, previous)

    def test_failed_reload_rolls_back_and_next_attempt_retries(self):
        self.install()
        previous = self.pointer
        (self.lineage / "fullchain.pem").write_bytes(b"DUMMY CERTIFICATE V2")
        self.run.side_effect = [subprocess.CompletedProcess([], 0), subprocess.CalledProcessError(1, ["reload"])]
        with self.assertRaises(subprocess.CalledProcessError):
            self.install()
        self.assertEqual(self.pointer, previous)
        self.run.side_effect = None
        self.assertTrue(self.install())
        self.assertNotEqual(self.pointer, previous)
        self.assertEqual(len([path for path in self.tls.iterdir() if path.is_dir()]), 2)

    def test_stopped_service_gets_files_without_being_started(self):
        self.run.return_value = subprocess.CompletedProcess([], 3)
        self.assertTrue(self.install())
        self.assertEqual(self.run.call_count, 1)
        self.assertIn("is-active", self.run.call_args.args[0])

    def test_refuses_to_overwrite_regular_current_path(self):
        self.current.write_text("unrelated file", encoding="ascii")
        with self.assertRaises(ValueError):
            self.install()
        self.assertEqual(self.current.read_text(encoding="ascii"), "unrelated file")


class AtomicPointerTests(unittest.TestCase):
    def test_pointer_is_replaced_with_a_single_atomic_rename(self):
        root = Path("/safe/tls")
        with patch.object(Path, "symlink_to") as symlink, patch.object(HOOK.os, "replace") as replace, \
                patch.object(Path, "is_symlink", return_value=False):
            HOOK.point_current(root, "new-bundle")
            symlink.assert_called_once_with("new-bundle", target_is_directory=True)
            replace.assert_called_once()
            self.assertEqual(replace.call_args.args[1], root / "current")
            self.assertEqual(replace.call_args.args[0].parent, root)


if __name__ == "__main__":
    unittest.main(verbosity=2)
