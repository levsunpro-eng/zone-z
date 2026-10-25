These keys and self-signed certificates are public, disposable test fixtures.
They identify localhost and 127.0.0.1 only. Never install them as OS trust roots
or use them for deployment. Tests trust them only in a temporary SSLContext.
The Oracle server archive excludes all test fixtures.

The source repository does not distribute private keys or certificates.
To run test_tls_reload.py, install OpenSSL and run these commands from server/:

openssl req -x509 -newkey rsa:2048 -nodes -keyout testdata/tls/key1.pem -out testdata/tls/cert1.pem -days 30 -subj "/CN=localhost" -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"
openssl req -x509 -newkey rsa:2048 -nodes -keyout testdata/tls/key2.pem -out testdata/tls/cert2.pem -days 30 -subj "/CN=localhost" -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"
python -m unittest test_tls_reload.py

Only run the generation commands when the named fixture files do not exist;
they overwrite files with the same names. Keep generated files local: the
repository .gitignore excludes them. Regenerate after their 30-day validity.
