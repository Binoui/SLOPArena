# ADR-0062: In-game feedback

Status: accepted for implementation, 2026-11-04.

Players need to report the moment they observed without leaving SLOPArena. F8,
match pause FEEDBACK and frontend MENU FEEDBACK open one persistent composer.
The implementation follows `/tmp/arena-feedback/synthesis.md` wire contract v1.

Shared owns immutable feedback values and the private multipart HTTP codec.
The metadata allowlist contains version, scene, capture time and available match
and admitted fighter details. It contains no account fields or logs. Master derives
identity from the existing Bearer JWT. ChatSession signs in on demand without a
chat connection or chosen display name. Feedback has its own 20-second HTTP
client; other Master requests retain their five-second timeout.

The overlay takes input and existing local pause ownership, hides the pause panel,
and captures at end of frame before building the composer. Online matches continue.
A failed capture opens a text-only composer. Capture and context belong to one
memory-only draft. First Send seals the message, kind, image selection and UUID.
Explicit Retry sends identical evidence and UUID. Close retains it, Discard clears
it, account changes clear it, and quitting loses it. No disk outbox or automatic
retry is introduced. Steam's modal gamepad keyboard is optional; sending requires
no typing. The screenshot can include names, HUD and visible chat.

Master receives `POST /feedback` multipart metadata plus optional JPEG. Accepted
200/201 replies carry a receipt. Missing deployment (404), authentication, quota,
network uncertainty and service failures retain the draft for explicit retry.
Invalid content is rejected for explicit discard. Slop Factory pulls accepted
reports privately and owns triage; client code contains no owner credentials.

Alternatives were an external web form, logs, or a persistent upload queue. The
approved slice keeps evidence tied to the visible frame and bounds retention to
one draft. It does not change simulation messages or protocol version. The usual
forward protocol bump belongs to the next release, together with owner-gated
Master deployment and client release. Clients released first show availability
failure and retain their draft.
