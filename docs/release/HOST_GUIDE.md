# SLOPArena — hosting your own game

Hosting starts an embedded game server on your machine. Others can join you
over the internet ONLY if your router forwards traffic to you.

1. Router: forward TCP 7777 and UDP 7777-7791 to this PC.
2. Check your public IP (https://ifconfig.me) and confirm your ISP doesn't
   use CGNAT (public IP must equal your router's WAN IP).
3. In the game, click Host and enter your public IP or domain.
4. Share the server name — friends find it in the Join list.

The game server is bundled inside the game — no .NET or extra installs needed.
