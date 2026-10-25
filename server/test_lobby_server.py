import asyncio
import json
import struct
import unittest

from lobby_server import LobbyServer, MAX_FRAME, encode


class WireClient:
    def __init__(self, reader, writer):
        self.reader, self.writer = reader, writer
        self.id = None

    async def send(self, **message):
        self.writer.write(encode(message))
        await self.writer.drain()

    async def receive(self, service):
        size = struct.unpack("<I", await asyncio.wait_for(self.reader.readexactly(4), 3))[0]
        message = json.loads(await asyncio.wait_for(self.reader.readexactly(size), 3))
        if message["service"] != service:
            raise AssertionError("Expected %s, got %r" % (service, message))
        return message

    async def close(self):
        self.writer.close()
        await self.writer.wait_closed()


class LobbyTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.server = LobbyServer(max_connections=32)
        self.port = await self.server.start(port=0)
        self.clients = []

    async def asyncTearDown(self):
        for client in self.clients:
            await client.close()
        await self.server.close()

    async def connect(self):
        reader, writer = await asyncio.wait_for(asyncio.open_connection("127.0.0.1", self.port), 5)
        client = WireClient(reader, writer)
        self.clients.append(client)
        client.id = (await client.receive("welcome"))["id"]
        return client

    async def create(self, client, name="Player", mode="team", version="test-1"):
        await client.send(service="create", name=name, mode=mode, version=version)
        return await client.receive("lobby")

    async def test_invitation_permissions_and_leader_transfer(self):
        leader, friend = await self.connect(), await self.connect()
        lobby = await self.create(leader, name="Капитан")
        self.assertEqual(len(lobby["code"]), 8)
        self.assertEqual(lobby["selfId"], leader.id)
        await friend.send(service="join", code=lobby["code"].lower(), name="Друг", version="test-1")
        joined = await friend.receive("lobby")
        await leader.receive("lobby")
        self.assertEqual([p["name"] for p in joined["players"]], ["Капитан", "Друг"])
        await friend.send(service="search")
        self.assertEqual((await friend.receive("error"))["error"], "invalid_request")
        await leader.send(service="leave")
        await leader.receive("left")
        remaining = await friend.receive("lobby")
        self.assertEqual(remaining["leader"], friend.id)
        self.assertEqual(len(remaining["players"]), 1)

    async def test_human_match_relay_isolation_and_host_loss(self):
        host, mate, opponent, outsider = [await self.connect() for _ in range(4)]
        lobby = await self.create(host)
        await mate.send(service="join", code=lobby["code"], name="Mate", version="test-1")
        await mate.receive("lobby")
        await host.receive("lobby")
        await self.create(opponent, name="Opponent")
        await host.send(service="search")
        self.assertTrue((await host.receive("lobby"))["searching"])
        await mate.receive("lobby")
        self.assertIsNone(self.server.clients[host.id].match, "A waiting party cannot match fictional opponents")
        await opponent.send(service="search")
        await opponent.receive("lobby")
        starts = [await player.receive("start") for player in (host, mate, opponent)]
        self.assertEqual([s["role"] for s in starts], ["host", "client", "client"])
        self.assertFalse(any(s["bots"] for s in starts))
        self.assertEqual(len({s["code"] for s in starts}), 1)
        self.assertEqual([p["team"] for p in starts[0]["players"]], [0, 0, 1])
        await opponent.send(service="game", peer=0, payload='{"hello":"opponent"}')
        relay = await host.receive("game")
        self.assertEqual(relay["peer"], opponent.id)
        await host.send(service="game", peer=mate.id, payload='{"snapshot":1}')
        self.assertEqual((await mate.receive("game"))["peer"], 0)
        await mate.send(service="game", peer=opponent.id, payload="forged")
        await mate.receive("error")
        await outsider.send(service="game", peer=host.id, payload="injected")
        await outsider.receive("error")
        await mate.send(service="leave")
        await mate.receive("left")
        self.assertEqual((await host.receive("peer_left"))["peer"], mate.id)
        await host.close()
        await opponent.receive("ended")
        self.assertFalse(self.server.matches)
        await self.create(opponent, name="Can play again")

    async def test_cancel_version_isolation_and_explicit_bots(self):
        first, second, friend = [await self.connect() for _ in range(3)]
        lobby = await self.create(first, version="v1")
        await friend.send(service="join", code=lobby["code"], name="Wrong version", version="v2")
        self.assertEqual((await friend.receive("error"))["error"], "version_mismatch")
        await self.create(second, version="v2")
        await first.send(service="search")
        await first.receive("lobby")
        await second.send(service="search")
        await second.receive("lobby")
        self.assertFalse(self.server.matches)
        await friend.send(service="join", code=lobby["code"], name="Friend", version="v1")
        self.assertEqual((await friend.receive("error"))["error"], "searching")
        await first.send(service="cancel")
        self.assertFalse((await first.receive("lobby"))["searching"])
        await first.send(service="bots")
        start = await first.receive("start")
        self.assertTrue(start["bots"])
        self.assertEqual(len(start["players"]), 1)
        self.assertNotIn(("team", "v1"), self.server.queues)

    async def test_mode_isolation_and_deathmatch_teams(self):
        team, dm1, dm2 = [await self.connect() for _ in range(3)]
        for client, mode in ((team, "team"), (dm1, "deathmatch"), (dm2, "deathmatch")):
            await self.create(client, mode=mode)
        for client in (team, dm1):
            await client.send(service="search")
            await client.receive("lobby")
        self.assertFalse(self.server.matches)
        await dm2.send(service="search")
        await dm2.receive("lobby")
        start = await dm1.receive("start")
        await dm2.receive("start")
        self.assertEqual([p["team"] for p in start["players"]], [0, 1])
        self.assertIsNone(self.server.clients[team.id].match)

    async def test_bad_frames_disconnect_and_other_client_survives(self):
        good = await self.connect()
        for bad_data in (struct.pack("<I", MAX_FRAME + 1), struct.pack("<I", 0),
                         struct.pack("<I", 2) + b"\xc3(", struct.pack("<I", 3) + b"NaN"):
            bad = await self.connect()
            bad.writer.write(bad_data)
            await bad.writer.drain()
            self.assertEqual(await asyncio.wait_for(bad.reader.read(1), 3), b"")
        await good.send(service="ping")
        await good.receive("pong")

    async def test_party_capacity_and_leave_unqueues(self):
        leader = await self.connect()
        lobby = await self.create(leader)
        party = [leader]
        for i in range(11):
            client = await self.connect()
            await client.send(service="join", code=lobby["code"], name="Friend%d" % i, version="test-1")
            await client.receive("lobby")
            for existing in party:
                await existing.receive("lobby")
            party.append(client)
        extra = await self.connect()
        await extra.send(service="join", code=lobby["code"], name="Overflow", version="test-1")
        self.assertEqual((await extra.receive("error"))["error"], "full")
        await leader.send(service="search")
        for client in party:
            await client.receive("lobby")
        await party[-1].send(service="leave")
        await party[-1].receive("left")
        for client in party[:-1]:
            self.assertFalse((await client.receive("lobby"))["searching"])
        self.assertFalse(self.server.queues)

    async def test_content_hash_version_and_connection_limit(self):
        self.server.max_connections = 2
        first, second = await self.connect(), await self.connect()
        reader, writer = await asyncio.wait_for(asyncio.open_connection("127.0.0.1", self.port), 5)
        self.assertEqual(await asyncio.wait_for(reader.read(1), 3), b"")
        writer.close()
        await writer.wait_closed()
        version = "ABCDEF0123456789" * 4
        lobby = await self.create(first, version=version)
        await second.send(service="join", code=lobby["code"], name="Same content", version=version)
        self.assertEqual((await second.receive("lobby"))["version"], version)
        await first.receive("lobby")


if __name__ == "__main__":
    unittest.main(verbosity=2)
