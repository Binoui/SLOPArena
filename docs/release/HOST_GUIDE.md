# SLOPArena — playing online with friends

Online matches use Rooms and a dedicated GameServer.

1. Keep Steam running and sign in with access to the SLOPArena Playtest.
   Everyone needs a compatible client build.
2. Open **Online** and create a Room, or join your friend's Room.
3. The Room leader chooses the stage and match format, then players choose
   their fighters and colours.
4. Each player locks in. Once everyone is locked in, the leader clicks
   **START MATCH**.

Creating a Room does not start a server on your PC. Players do not need to
configure router port forwarding for this flow. The online services and a
compatible GameServer must be available for the match to start.

The embedded **Host** and direct-connect controls are development tools. They
are available only in the Unity Editor with `SLOPARENA_DEV_UDP=1`; they are not
controls in the distributed client. Repository contributors should follow
`docs/systems/netcode-architecture.md` and the operations workflow for development
hosting and connection diagnostics.
