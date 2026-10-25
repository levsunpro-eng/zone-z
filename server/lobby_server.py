"""ZoneZ invitation lobbies, human matchmaking and bounded TCP game relay.

Python 3.9+, standard library only. See README.md for the wire protocol and TLS.
"""
import argparse
import asyncio
import contextlib
import json
import logging
import secrets
import signal
import ssl
import struct
import time
from collections import deque
from dataclasses import dataclass, field

MAX_FRAME = 512 * 1024
MAX_OUT_BYTES = 2 * 1024 * 1024
MAX_OUT_MESSAGES = 128
IDLE_TIMEOUT = 90
CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
LOG = logging.getLogger("zonez.lobby")


class ProtocolError(Exception):
    pass


class ReloadableTls:
    """Select a fully loaded certificate for new handshakes without dropping peers."""

    def __init__(self, certificate, key):
        self.certificate, self.key = certificate, key
        self.active = self.load_context()
        self.context = self.active
        # The callback also runs without SNI (e.g. clients connecting by IP).
        # Switching the handshake's context leaves established sessions intact.
        self.context.sni_callback = self.select_context

    def load_context(self):
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.minimum_version = ssl.TLSVersion.TLSv1_2
        context.load_cert_chain(self.certificate, self.key)
        return context

    def select_context(self, connection, _name, _initial):
        connection.context = self.active

    def reload(self):
        try:
            replacement = self.load_context()
        except (OSError, ssl.SSLError):
            LOG.exception("TLS certificate reload failed; keeping the previous certificate.")
            return False
        self.active = replacement
        LOG.info("TLS certificate reloaded; established connections are unchanged.")
        return True


@dataclass(eq=False)
class Client:
    id: int
    reader: asyncio.StreamReader
    writer: asyncio.StreamWriter
    name: str = ""
    room: object = None
    match: object = None
    closed: bool = False
    closing: bool = False
    outgoing: asyncio.Queue = field(default_factory=lambda: asyncio.Queue(MAX_OUT_MESSAGES))
    out_bytes: int = 0
    writer_task: object = None
    tokens: float = 2000.0
    byte_tokens: float = float(32 * 1024 * 1024)
    token_time: float = field(default_factory=time.monotonic)


@dataclass(eq=False)
class Room:
    code: str
    mode: str
    version: str
    leader: Client
    players: list = field(default_factory=list)
    searching: bool = False


@dataclass(eq=False)
class Match:
    code: str
    host: Client
    players: dict


def encode(message):
    try:
        body = json.dumps(message, ensure_ascii=False, allow_nan=False, separators=(",", ":")).encode("utf-8")
    except (UnicodeError, TypeError, ValueError, RecursionError) as exc:
        raise ProtocolError("Invalid outgoing JSON content.") from exc
    if len(body) > MAX_FRAME:
        raise ProtocolError("Message exceeds 512 KiB.")
    return struct.pack("<I", len(body)) + body


def clean_text(value, limit, label):
    if not isinstance(value, str) or not value.strip() or len(value) > limit:
        raise ProtocolError("Invalid " + label + ".")
    if any(ord(char) < 32 or ord(char) == 127 or 0xD800 <= ord(char) <= 0xDFFF for char in value):
        raise ProtocolError("Invalid " + label + ".")
    return value.strip()


def reject_constant(value):
    raise ValueError("Invalid JSON constant " + value)


class LobbyServer:
    def __init__(self, max_connections=256):
        self.max_connections = max_connections
        self.clients = {}
        self.rooms = {}
        self.matches = {}
        self.queues = {}
        self.next_id = 1
        self.server = None
        self.handlers = set()

    async def start(self, host="127.0.0.1", port=7780, ssl_context=None):
        options = {"limit": MAX_FRAME + 4}
        if ssl_context is not None:
            options.update(ssl=ssl_context, ssl_handshake_timeout=8)
        self.server = await asyncio.start_server(self.handle_client, host, port, **options)
        return self.server.sockets[0].getsockname()[1]

    async def close(self):
        if self.server is not None:
            self.server.close()
            await self.server.wait_closed()
        for client in list(self.clients.values()):
            self.disconnect(client, "Server shutting down.")
        if self.handlers:
            await asyncio.gather(*list(self.handlers), return_exceptions=True)

    async def handle_client(self, reader, writer):
        task = asyncio.current_task()
        self.handlers.add(task)
        client = None
        try:
            if len(self.clients) >= self.max_connections:
                return
            client = Client(self.next_id, reader, writer)
            self.next_id += 1
            self.clients[client.id] = client
            client.writer_task = asyncio.create_task(self.write_loop(client))
            self.emit(client, {"service": "welcome", "id": client.id, "protocol": 1})
            while not client.closed:
                header = await asyncio.wait_for(reader.readexactly(4), IDLE_TIMEOUT)
                size = struct.unpack("<I", header)[0]
                if not 0 < size <= MAX_FRAME:
                    raise ProtocolError("Invalid frame length.")
                self.check_rate(client, size)
                body = await asyncio.wait_for(reader.readexactly(size), 15)
                try:
                    message = json.loads(body.decode("utf-8"), parse_constant=reject_constant)
                except (UnicodeError, ValueError, RecursionError) as exc:
                    raise ProtocolError("Invalid JSON message.") from exc
                if not isinstance(message, dict):
                    raise ProtocolError("A message must be a JSON object.")
                try:
                    self.dispatch(client, message)
                except ProtocolError as exc:
                    self.error(client, "invalid_request", str(exc))
        except (asyncio.IncompleteReadError, ConnectionError, asyncio.TimeoutError, ProtocolError, OSError):
            pass
        finally:
            if client is not None:
                self.disconnect(client, "Connection closed.")
                if client.writer_task is not None:
                    with contextlib.suppress(asyncio.CancelledError, ConnectionError, OSError):
                        await client.writer_task
            writer.close()
            with contextlib.suppress(Exception):
                await asyncio.wait_for(writer.wait_closed(), 2)
            self.handlers.discard(task)

    def check_rate(self, client, size):
        now = time.monotonic()
        elapsed = now - client.token_time
        client.token_time = now
        client.tokens = min(2000, client.tokens + elapsed * 1000)
        client.byte_tokens = min(32 * 1024 * 1024, client.byte_tokens + elapsed * 16 * 1024 * 1024)
        if client.tokens < 1 or client.byte_tokens < size:
            raise ProtocolError("Rate limit exceeded.")
        client.tokens -= 1
        client.byte_tokens -= size

    def emit(self, client, message):
        if client.closed or client.closing:
            return
        frame = encode(message)
        if client.outgoing.full() or client.out_bytes + len(frame) > MAX_OUT_BYTES:
            # Defer membership changes until the current broadcast/start has
            # finished, preserving start-before-ended ordering for all peers.
            client.closing = True
            client.writer.close()
            asyncio.get_running_loop().call_soon(self.disconnect, client, "Slow receiver.")
            return
        client.outgoing.put_nowait(frame)
        client.out_bytes += len(frame)

    async def write_loop(self, client):
        try:
            while not client.closed:
                frame = await client.outgoing.get()
                client.out_bytes -= len(frame)
                client.writer.write(frame)
                await asyncio.wait_for(client.writer.drain(), 10)
        except (ConnectionError, OSError, asyncio.TimeoutError):
            self.disconnect(client, "Slow or disconnected receiver.")

    def error(self, client, code, message):
        self.emit(client, {"service": "error", "error": code, "message": message})

    def dispatch(self, client, message):
        action = message.get("service")
        if action == "ping":
            self.emit(client, {"service": "pong"})
        elif action == "create":
            self.require_free(client)
            name = clean_text(message.get("name"), 24, "player name")
            version = clean_text(message.get("version"), 128, "game version")
            mode = message.get("mode")
            if mode not in ("team", "deathmatch"):
                raise ProtocolError("Unknown game mode.")
            client.name = name
            room = Room(self.new_code(), mode, version, client, [client])
            self.rooms[room.code] = room
            client.room = room
            self.broadcast_room(room)
        elif action == "join":
            self.require_free(client)
            code = clean_text(message.get("code"), 8, "invitation code").upper()
            room = self.rooms.get(code)
            name = clean_text(message.get("name"), 24, "player name")
            version = clean_text(message.get("version"), 128, "game version")
            if room is None:
                self.error(client, "not_found", "Lobby not found.")
            elif room.version != version:
                self.error(client, "version_mismatch", "Players must use the same game version.")
            elif room.searching:
                self.error(client, "searching", "The leader must stop searching before friends can join.")
            elif len(room.players) >= (12 if room.mode == "team" else 20):
                self.error(client, "full", "Lobby is full.")
            else:
                client.name = name
                client.room = room
                room.players.append(client)
                self.broadcast_room(room)
        elif action in ("search", "cancel"):
            room = self.require_leader(client)
            enabled = message.get("searching", True) if action == "search" else False
            if not isinstance(enabled, bool):
                raise ProtocolError("searching must be a boolean.")
            if enabled and room.mode == "deathmatch" and len(room.players) == 20:
                raise ProtocolError("Leave at least one slot for an opposing party.")
            self.unqueue(room)
            if enabled:
                room.searching = True
                self.queues.setdefault((room.mode, room.version), deque()).append(room)
            self.broadcast_room(room)
            if enabled:
                self.try_match(room.mode, room.version)
        elif action == "bots":
            room = self.require_leader(client)
            self.start_match([room], bots=True)
        elif action == "leave":
            self.leave(client, "Player left.")
            self.emit(client, {"service": "left"})
        elif action == "game":
            self.relay(client, message)
        else:
            raise ProtocolError("Unknown service action.")

    @staticmethod
    def require_free(client):
        if client.room is not None or client.match is not None:
            raise ProtocolError("Leave the current lobby or match first.")

    @staticmethod
    def require_leader(client):
        if client.room is None or client.room.leader is not client:
            raise ProtocolError("Only the lobby leader can perform this action.")
        return client.room

    def new_code(self):
        while True:
            code = "".join(secrets.choice(CODE_ALPHABET) for _ in range(8))
            if code not in self.rooms and code not in self.matches:
                return code

    @staticmethod
    def player(client, team=0):
        return {"id": client.id, "name": client.name, "team": team}

    def broadcast_room(self, room):
        message = {"service": "lobby", "code": room.code, "leader": room.leader.id,
                   "mode": room.mode, "version": room.version, "searching": room.searching,
                   "players": [self.player(player) for player in room.players]}
        for player in list(room.players):
            self.emit(player, dict(message, selfId=player.id))

    def unqueue(self, room):
        room.searching = False
        key = (room.mode, room.version)
        queue = self.queues.get(key)
        if queue is not None:
            with contextlib.suppress(ValueError):
                queue.remove(room)
            if not queue:
                self.queues.pop(key, None)

    def try_match(self, mode, version):
        queue = self.queues.get((mode, version))
        if queue is None:
            return
        parties = list(queue)
        for index, first in enumerate(parties):
            for second in parties[index + 1:]:
                if mode == "team" or len(first.players) + len(second.players) <= 20:
                    self.start_match([first, second], bots=False)
                    self.try_match(mode, version)
                    return

    def start_match(self, rooms, bots):
        first = rooms[0]
        host = first.leader
        players = [player for room in rooms for player in room.players]
        roster = []
        for team, room in enumerate(rooms):
            for player in room.players:
                roster.append(self.player(player, team if first.mode == "team" else len(roster)))
        match = Match(self.new_code(), host, {player.id: player for player in players})
        self.matches[match.code] = match
        for room in rooms:
            self.unqueue(room)
            self.rooms.pop(room.code, None)
        for player in players:
            player.room = None
            player.match = match
        # No awaits: every participant's start is enqueued before a game frame
        # can be processed from the host or another client.
        for player in players:
            self.emit(player, {"service": "start", "role": "host" if player is host else "client",
                               "peerId": player.id, "leader": host.id, "players": roster,
                               "code": match.code, "mode": first.mode, "version": first.version, "bots": bots})

    def relay(self, client, message):
        match = client.match
        if match is None:
            raise ProtocolError("No active match.")
        peer = message.get("peer")
        payload = message.get("payload")
        if isinstance(peer, bool) or not isinstance(peer, int) or not isinstance(payload, str):
            raise ProtocolError("Invalid game envelope.")
        if client is match.host:
            target = match.players.get(peer)
            if target is None or target is client:
                raise ProtocolError("Unknown game peer.")
            self.emit(target, {"service": "game", "peer": 0, "payload": payload})
        else:
            if peer != 0:
                raise ProtocolError("Clients may only send game data to the host.")
            self.emit(match.host, {"service": "game", "peer": client.id, "payload": payload})

    def leave(self, client, reason):
        room = client.room
        client.room = None
        if room is not None:
            self.unqueue(room)
            room.players.remove(client)
            if room.players:
                if room.leader is client:
                    room.leader = room.players[0]
                self.broadcast_room(room)
            else:
                self.rooms.pop(room.code, None)
        match = client.match
        client.match = None
        if match is not None:
            match.players.pop(client.id, None)
            if client is match.host:
                self.matches.pop(match.code, None)
                for player in list(match.players.values()):
                    player.match = None
                    self.emit(player, {"service": "ended", "message": "The match host disconnected."})
                match.players.clear()
            else:
                self.emit(match.host, {"service": "peer_left", "peer": client.id, "message": reason})

    def disconnect(self, client, reason):
        if client.closed:
            return
        client.closed = True
        self.clients.pop(client.id, None)
        self.leave(client, reason)
        client.writer.close()
        if client.writer_task is not None and client.writer_task is not asyncio.current_task():
            client.writer_task.cancel()


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7780)
    parser.add_argument("--max-connections", type=int, default=256)
    parser.add_argument("--cert", help="PEM certificate chain, including the server certificate")
    parser.add_argument("--key", help="PEM private key")
    parser.add_argument("--allow-plaintext", action="store_true", help="Explicitly permit non-TLS LAN/development use")
    args = parser.parse_args()
    if not 1 <= args.port <= 65535 or not 2 <= args.max_connections <= 4096:
        parser.error("Invalid port or connection limit.")
    if bool(args.cert) != bool(args.key):
        parser.error("--cert and --key are required together.")
    if args.host not in ("127.0.0.1", "::1", "localhost") and not args.cert and not args.allow_plaintext:
        parser.error("Public binding requires --cert/--key; use --allow-plaintext only for controlled LAN testing.")
    return args


async def main(args):
    certificates = ReloadableTls(args.cert, args.key) if args.cert else None
    tls = certificates.context if certificates else None
    service = LobbyServer(args.max_connections)
    await service.start(args.host, args.port, tls)
    loop = asyncio.get_running_loop()
    reload_signal = None
    if certificates and hasattr(signal, "SIGHUP"):
        try:
            loop.add_signal_handler(signal.SIGHUP, certificates.reload)
            reload_signal = signal.SIGHUP
        except (NotImplementedError, RuntimeError):
            LOG.warning("TLS hot reload is unavailable on this platform.")
    LOG.info("Listening on %s:%s (%s)", args.host, args.port, "TLS" if tls else "plaintext")
    try:
        async with service.server:
            await service.server.serve_forever()
    finally:
        if reload_signal is not None:
            loop.remove_signal_handler(reload_signal)
        await service.close()


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    try:
        asyncio.run(main(arguments()))
    except KeyboardInterrupt:
        pass
