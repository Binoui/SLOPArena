# Security Policy

## Reporting a Vulnerability

SLOPArena is a community-driven open-source game. We take security
concerns seriously.

If you discover a security vulnerability, do not publish its details in a regular
GitHub issue or public chat. Use the [project contact information](../../README.md#everything-else)
to contact Binoui privately and request a private reporting channel before sharing
reproduction details or credentials. This guide does not currently name a dedicated
security email or verified private vulnerability-reporting endpoint.

We will acknowledge receipt within 48 hours and provide an estimated
timeline for a fix.

## Scope

This policy covers:
- The game client (Unity 6 C# project)
- The headless server (`src/Server/`)
- The Shared C# library (`src/Shared/`)
- CI/CD workflows and infrastructure

## Out of Scope

- Issues in Unity Editor itself (report upstream)
- Vulnerabilities in .NET runtime (report to Microsoft)
- General feature requests or bugs (open a regular issue)

## Preferred Format

When reporting, please include:
1. Description of the vulnerability
2. Steps to reproduce
3. Potential impact
4. Suggested fix (if any)

We appreciate your help keeping SLOPArena safe for everyone!

## Secret scanning

CI scans the full Git history with redacted Gitleaks output. The root
`.gitleaksignore` exempts only two exact historical fingerprints for the same
explicit, deterministic JWT test-secret example in the local-test walkthrough.
These examples are not deployment credentials and must never be used on a live
service. New findings remain blocking; never exempt a file or credential pattern
to hide a real leak. Rotate any exposed live credential through the private
operator configuration before treating removal from the current tree as a fix.
