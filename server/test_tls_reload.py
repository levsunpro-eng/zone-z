"""TLS rotation integration tests; certificates in testdata are disposable fixtures."""
import asyncio
import contextlib
import json
from pathlib import Path
import shutil
import ssl
import struct
import tempfile
import unittest

from lobby_server import LobbyServer, ReloadableTls, encode

FIXTURES = Path(__file__).parent / "testdata" / "tls"


class TlsReloadTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="zonez-tls-test-")
        self.directory = Path(self.temporary.name)
        self.select_files(1)
        self.tls = ReloadableTls(str(self.directory / "cert.pem"), str(self.directory / "key.pem"))
        self.server = LobbyServer(max_connections=8)
        self.port = await self.server.start(port=0, ssl_context=self.tls.context)
        self.connections = []
        self.client_context = ssl.create_default_context()
        for number in (1, 2):
            self.client_context.load_verify_locations(str(FIXTURES / ("cert%d.pem" % number)))

    async def asyncTearDown(self):
        for _reader, writer in self.connections:
            writer.close()
        for _reader, writer in self.connections:
            # Python 3.9 may report a pending lobby notification after our
            # close_notify during simultaneous fixture cleanup.
            with contextlib.suppress(ssl.SSLError, ConnectionError):
                await asyncio.wait_for(writer.wait_closed(), 5)
        await self.server.close()
        self.temporary.cleanup()

    def select_files(self, certificate, key=None):
        shutil.copyfile(FIXTURES / ("cert%d.pem" % certificate), self.directory / "cert.pem")
        shutil.copyfile(FIXTURES / ("key%d.pem" % (key or certificate)), self.directory / "key.pem")

    async def receive(self, reader, expected):
        header = await asyncio.wait_for(reader.readexactly(4), 3)
        size = struct.unpack("<I", header)[0]
        self.assertLessEqual(size, 512 * 1024)
        result = json.loads(await asyncio.wait_for(reader.readexactly(size), 3))
        self.assertEqual(result["service"], expected)
        return result

    async def send(self, writer, **message):
        writer.write(encode(message))
        await writer.drain()

    async def connect(self, expected_certificate):
        # A numeric server_hostname validates IP SAN; it sends no DNS SNI name.
        reader, writer = await asyncio.wait_for(asyncio.open_connection(
            "127.0.0.1", self.port, ssl=self.client_context, server_hostname="127.0.0.1"), 5)
        self.connections.append((reader, writer))
        expected = ssl.PEM_cert_to_DER_cert((FIXTURES / ("cert%d.pem" % expected_certificate)).read_text())
        self.assertEqual(writer.get_extra_info("ssl_object").getpeercert(binary_form=True), expected)
        welcome = await self.receive(reader, "welcome")
        self.assertEqual(welcome["protocol"], 1)
        return reader, writer

    async def test_rotation_preserves_established_connection_and_party(self):
        first_reader, first_writer = await self.connect(1)
        await self.send(first_writer, service="create", name="Before", mode="team", version="rotation-test")
        lobby = await self.receive(first_reader, "lobby")
        self.select_files(2)
        self.assertTrue(self.tls.reload())
        second_reader, second_writer = await self.connect(2)
        await self.send(second_writer, service="join", name="After", code=lobby["code"], version="rotation-test")
        second_lobby = await self.receive(second_reader, "lobby")
        first_lobby = await self.receive(first_reader, "lobby")
        self.assertEqual(first_lobby["code"], lobby["code"])
        self.assertEqual(second_lobby["code"], lobby["code"])
        self.assertEqual(len(first_lobby["players"]), 2)
        await self.send(first_writer, service="ping")
        await self.receive(first_reader, "pong")

    async def test_invalid_rotation_keeps_previous_certificate_and_peers(self):
        reader, writer = await self.connect(1)
        previous = self.tls.active
        self.select_files(2, key=1)
        with self.assertLogs("zonez.lobby", level="ERROR"):
            self.assertFalse(self.tls.reload())
        self.assertIs(self.tls.active, previous)
        await self.connect(1)
        await self.send(writer, service="ping")
        await self.receive(reader, "pong")
        self.select_files(2)
        self.assertTrue(self.tls.reload())
        await self.connect(2)


if __name__ == "__main__":
    unittest.main(verbosity=2)
