# Local account acceptance

## Web and Android

1. Sign in with an existing account. Open Akun (web) or Settings → Password,
   recovery codes & invitations (Android). Enter the current password, create
   an invitation, and give that code to the new user. Wrong password must fail.
2. Sign out/use a separate browser profile. Open Daftar/Create account, enter
   username, password of 12–256 characters, matching confirmation and invitation.
3. Save the eight recovery codes in a password manager or independent safe place.
   Plaintext is displayed only for this operation. Web supports copy/download;
   Android explicit clipboard copy marks the content sensitive.
4. Log in normally. New accounts have no workbook until normal onboarding;
   generated books must use the approved neutral registration template.
   An invitation cannot register a second account.
5. Open Lupa password/Pulihkan on another browser/device. Submit username, one
   saved code and a new matching password. Old browser and Android server sessions
   must end, the old password must fail and the new password must log in.
   Local pending Android commands are not deleted by recovery.
6. Used code and every other code from that old set must fail. After logging in,
   generate a new set with password reauthentication. A wrong password must not
   replace the valid set. Changing a known password preserves recovery codes.
7. Test password mismatch, too short/long input, invalid/expired invitation,
   unavailable username, wrong recovery code, cancelled/back navigation and
   network failure. Do not assume a timed-out mutation was not committed.
8. Android sensitive account screens must set `FLAG_SECURE`; do not disable it
   to obtain screenshots of real recovery codes. Password/code drafts use memory,
   not saved state, Room or logs. Returning to Login/Settings clears result codes.

## Automated checks

- `AccountRegistrationTests`: account isolation, atomic invitation use/races,
  preservation after failed signup, recovery and session revocation,
  reauthentication, invitation quotas/expiry/disabled issuer, legacy schema
  upgrade and refusal to fall back to the private template.
- `AccountBrowserTests`: real Razor forms, antiforgery, one-time code display,
  browser session invalidation, regeneration, invitations and password change.
- `test_registration_template.py`: only selected literal defaults change;
  every selected numeric literal is zero, including a paired cell after a
  self-closing empty cell; unrelated parts/shared strings/VBA remain byte-identical.
- Android JVM test verifies password length and matching confirmation.
- Opt-in `AccountFlowUiTest`: synthetic local backend only at emulator host
  `127.0.0.1:5088` via an emulator-only `adb reverse tcp:5088 tcp:5088`.
  Seed a disposable `ui-owner` with the test password from the
  test, run on an isolated emulator with
  `-e lajur.runAccountUiSmokeTests true`. It exercises actual register/recover
  UI plus verifies `FLAG_SECURE` and successful login after recovery.
  API 37 requires platform keyboard input with an IME restart wait; the current
  Espresso version uses a removed hidden InputManager method. Failed runs can
  leave unused invitations in disposable storage; reset that local fixture if
  its five-invitation quota is exhausted. Never reset production metadata for a test.
  This optional UI smoke is not part of mandatory CI.

Never test forgotten-password recovery on the owner's production account.
Use a dedicated synthetic account or isolated local API storage.
