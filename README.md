# یه‌وجب جا | Yek Vajab Ja

A Persian casual parking game built with Unity WebGL. Players make three parking attempts, earn a score, and can challenge friends with a shareable result.

## Repository layout

- `ye-vajab-ja/` — Unity project source, assets, packages, and project settings.
- `backend/` — edge challenge API and local development server.
- `PROJECT_GUIDE.md` — project decisions and current gameplay rules.

## Open the Unity project

Use Unity Hub to add and open the `ye-vajab-ja` folder. The project was developed with Unity `6000.5.5f1`.

## Run locally

Install Node.js, then run `Run-Local-Web.bat` to start the local web server. Open the address printed by the server in a browser.

## Build for WebGL

In Unity, use **Tools > Yek Vajab Ja > Build Web Optimized**. Generated build files are intentionally excluded from this source repository; create a fresh build when preparing a release.

## Deployment secrets

Set `CHALLENGE_HMAC_KEY` as a private environment secret in the edge function. Never add it to Unity assets, web files, or this repository. See `backend/README.md` for API and deployment notes.
