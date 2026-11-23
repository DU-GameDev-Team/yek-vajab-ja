# Stateless challenge API

`challenge-api.mjs` exports a Fetch API handler compatible with standard edge runtimes:

```js
import { handleRequest } from "./challenge-api.mjs";
export default {
  fetch(request, env) {
    return handleRequest(request, env.CHALLENGE_HMAC_KEY);
  }
};
```

Configure a private random `CHALLENGE_HMAC_KEY` of at least 32 characters in the edge function's secret environment. Do not put it in Unity, HTML, source maps, or a public repository. Route `/api/challenges` and `/api/challenges/*` to this handler and host the WebGL build at the same origin. The handler stores no user data or database rows; the signed token contains only version, seed, scores, and total. It checks all submitted tap values and recomputes scores server-side. Rule versions 1 through 7 remain available for older links; new games use version 7. Keep Unity and this API on the same versioned rule table.

The rules table keeps versions 1 through 7 so existing challenge links continue to work. Versions 6 and 7 use gaps `[112, 98, 84]`, amplitudes `[76, 82, 88]`, and durations `[2900, 2250, 1600]` ms; v6 uses car length `56.8973`, while v7 uses `62.4633`. Keep the current Unity rule version, API rule table, and WebGL template validation in sync.

The hosting-specific deployment wrapper and secret configuration must follow the current Arvan Edge Function contract. This source deliberately stays on the standard Fetch API so the game rules and signature validation are independent of provider-specific packaging.
