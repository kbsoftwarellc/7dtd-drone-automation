# 7daystodiemods.com page sources

The second publishing lane, beside `../nexus/`. Same mod, different site, different dialect.

| File | Goes in |
|---|---|
| `short.txt` | The site's short-description field (max 300 characters). |
| `description.md` | The main body of the mod page. |
| `changelog.txt` | The changelog field — **one block per version** on this site. |
| `../media/featured_1280x720.png` | The "featured" image slot (1280x720). |
| `../media/thumb_400x225.png` | The "thumbnail" image slot (400x225). |

## Captured, not authored

Captured from the live page on 2026-08-03 with
`~/7dtd-mods/tools/modsite_sync.py DroneAutomation --capture`, byte for byte. The page was written
straight into the site before this folder existed, so the repo had no record of its own text.

Continued lines end with a trailing `\` — the site collapses single newlines inside a paragraph, and
that backslash is what keeps the line breaks. Keep them.

`~/7dtd-mods/tools/modsite_audit.py DroneAutomation` compares this folder against the live page.

## Known drift on the live page, as of the capture

These are recorded here rather than silently fixed, because fixing them means writing to the site:

- **The page advertises v0.7.3 but the only file row is `DroneAutomation_v0.7.2_gameV3.0.0.zip`.**
  Adding a version and changelog on that site does not upload a build; the two are separate steps.
  The current repo build is `DroneAutomation_v0.7.3_gameV3.0.0-V3.1.zip`.
- **An unpublished draft revision is pending on the page.** Whatever is in it ships with the next
  publish, so review it before publishing anything else.
- The text still says **"for 7 Days to Die V3.0"**; the mod covers V3.0.0 through V3.1 as of 0.7.3.
- It does not yet mention that the repo is public with build instructions (`BUILD.md`), which is
  what the Nexus moderator actually asked for after the v0.7.0 quarantine.

## Why this is not the Nexus copy

- **The renderer eats links and tables.** No `[text](url)`, no `| a | b |`.
- **The short description caps at 300 characters**; a Nexus `short.txt` is allowed 350.
- **The changelog field holds one version's notes**, not the whole history like `../CHANGELOG.md`.
