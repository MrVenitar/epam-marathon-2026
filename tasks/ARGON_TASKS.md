# Tasks for Gemini 4 Argon

> **Для тебе (людини).** Давай Argon **одну задачу за раз**: скопіюй у чат розділ «Rules for every task»
> і текст однієї задачі (від `## Task N` до наступного `## Task`). Після кожної задачі перевір блок
> «Done when» і лише тоді давай наступну. Порядок: 1 → 2 → 3 → 4 → 5 → 6.
> Задачі 1–3 не залежать від AQA, 4–6 — це основа для AQA-домашки (Напрям 1).

Context: this repository is the EPAM IT Marathon 2026 restaurant app ("Green & Tasty"):
Angular frontend, .NET 10 AWS Lambda backend (`backend/restaurant-backend-app`), Terraform
(`infra/`), AQA framework (`aqa/restaurant-aqa-framework`). The **group pre-order owner feature**
(levels 1–3 of `homework-backend.md`) is already implemented and unit-tested; its contract and the
exact behaviour are in `backend/restaurant-backend-app/docs/GroupPreOrder.md`. Read that file first.

Deployed API (base URL, includes the stage):
`https://9xb633h11g.execute-api.us-west-2.amazonaws.com/develop`
(it changes after "Destroy all" + "Deploy all": take the current one from the summary of the
latest *Deploy all* run in GitHub Actions).

---

## Rules for every task

1. **Stay inside the files listed under "Files you may change".** If the task seems to need any
   other file, stop and report instead of editing it.
2. **Never change backend business code** (`dnapp/lambdas/**/Services`, `Handlers`, `Repositories`,
   `Entities`) or the workflows in `.github/workflows/`. If you think something there is wrong,
   describe it in your report (file, line, what you expected, what you saw).
3. **Do not invent API details.** The source of truth is, in this order: `docs/GroupPreOrder.md`,
   the handler code in `dnapp/lambdas/api-handler/Handlers`, then `docs/ApiRoutes.md`
   (ApiRoutes is partly outdated for older endpoints). Real routes are registered in
   `dnapp/lambdas/api-handler/Routing/RouteRegistry.cs`.
4. **JSON casing is mixed in this API.** New endpoints return camelCase; some older ones return
   PascalCase (e.g. `GET /guest/invites/{token}` → `GroupReservationId`,
   `POST /guest/sessions` → `SessionId`, `SessionToken`). Read response keys case-insensitively.
5. **No secrets in the repository**: no passwords, tokens or e-mails of real people. Generate test
   e-mails like `aqa.sample.<guid>@example.com`.
6. Keep the change small. One task = one commit, message in Conventional Commits style,
   e.g. `docs(api): document group pre-order endpoints in swagger`.
7. Finish with a short report: what you changed (file list), how you verified it (commands and
   their output), anything you could not do.

---

## Task 1 — Swagger: document the group pre-order endpoints (small)

**Goal.** `swagger_src/swagger-ui/swagger-endpoints.yaml` describes the owner endpoints exactly as
they work now.

**Files you may change:** `backend/restaurant-backend-app/swagger_src/swagger-ui/swagger-endpoints.yaml`

**Steps**
1. `servers[0].url` → the deployed API URL from the top of this file.
2. The submit endpoint is documented under the wrong path `/guest/groups/{groupReservationId}/submit`.
   Move it to `POST /group-reservations/{groupReservationId}/submit`; request body
   `{ "reservationId": string }`; 200 response = array of `{ dishId: string, quantity: integer }`.
3. Add `GET /reservations/{reservationId}/guest-session-id` → 200 `{ guestSessionId: string }`.
4. Make `GET /guest/groups/{groupReservationId}/sessions` and
   `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}` return the snapshot schema
   from `docs/GroupPreOrder.md` (`participants[]` with `id, type (Owner|Guest), name, items[], total,
   isSubmitted`, plus `total`). Define it once under `components/schemas` and `$ref` it twice.
5. For each of these four endpoints list the status codes from the table in `docs/GroupPreOrder.md`.

**Done when**
- `python -c "import yaml,sys; yaml.safe_load(open(sys.argv[1]))" backend/restaurant-backend-app/swagger_src/swagger-ui/swagger-endpoints.yaml` prints nothing (no error).
- `npx --yes @apidevtools/swagger-cli validate backend/restaurant-backend-app/swagger_src/swagger-ui/swagger-endpoints.yaml` says the file is valid.
- `git diff --stat` shows only that one file.

---

## Task 2 — Smoke test, part 1: owner set-up helpers (small)

**Goal.** A stand-alone Python script that can create an owner with a reservation and the owner's own
pre-order on the deployed API. Part 2 (Task 3) adds the guests and the checks.

**Files you may change:** `scripts/smoke_group_preorder.py` (new file)

**Requirements**
- Python 3.10+, **standard library only** (`urllib.request`, `json`, `uuid`, `datetime`). No `requests`.
- Base URL from the environment variable `API_URL`; exit with a clear message if it is missing.
- One helper `call(method, path, body=None, token=None) -> (status, parsed_json_or_text)`; it never raises on
  HTTP errors (return the status and body instead). Header `Authorization: Bearer <token>` when a token is given.
- Functions, each doing one thing and printing one line `OK  <what>` or `FAIL <what>: <status> <body>`:
  1. `register_and_sign_in()` → `POST /register` (unique e-mail, password like `Aqa-Smoke-1a!`,
     first/last name `Smoke`/`Owner`), then `POST /signin` → return the **`idToken`**.
  2. `find_table(token)` → `GET /locations`, take the first location, then
     `GET /available-tables?locationId=<id>&capacity=4&startTime=<tomorrow 19:00 UTC, ISO 8601>`;
     return the first table. (Check the exact query names in `Handlers/Tables/GetAvailableTablesHandler.cs`.)
  3. `create_reservation(token, table)` → `POST /reservations` with `tableId`, `startTime`,
     `endTime` (start + 90 min) and `guestCount: 4`; return the reservation id
     (check the request model in `Handlers/Reservations/CreateReservationHandler.cs`).
  4. `create_owner_order(token, reservation_id, dish_id)` → `POST /orders` with
     `{ "reservationId": ..., "orderItems": [ { "dishId": ..., "quantity": 1 } ] }`
     (dish id: the first dish from `GET /dishes`).
- `main()` runs 1→4 and exits with code 0 if all steps are OK, 1 otherwise.
- Never print tokens or passwords.

**Done when**
- `python -I -m py_compile scripts/smoke_group_preorder.py` succeeds.
- `API_URL=<deployed url> python scripts/smoke_group_preorder.py` prints four `OK` lines.
  (Windows PowerShell: `$env:API_URL="<url>"; python scripts/smoke_group_preorder.py`.)

---

## Task 3 — Smoke test, part 2: the whole group pre-order flow (medium)

**Goal.** Extend `scripts/smoke_group_preorder.py` so it proves levels 1–3 work on the deployed API.

**Files you may change:** `scripts/smoke_group_preorder.py`

**Flow to add after Task 2's steps** (each step = one `OK`/`FAIL` line; expected codes and
messages are in `docs/GroupPreOrder.md`):
1. `GET /reservations/{id}/invite-token` (owner) → `token`; `GET /guest/invites/{token}` →
   `GroupReservationId` (PascalCase!).
2. Three guests join: `POST /guest/sessions` `{ groupReservationId, guestName }` → `SessionId`,
   `SessionToken`. Guest calls use `Authorization: Bearer <SessionToken>`.
   - Guest **A**: `POST /guest/sessions/{SessionId}/dishes` `{ dishId: D1, quantity: 2 }`, then
     `POST /guest/sessions/{SessionId}/submit`.
   - Guest **B**: adds `{ D2, 1 }`, does **not** submit.
   - Guest **C**: adds `{ D1, 1 }` and submits.
   (D1, D2 = two different dishes from `GET /dishes`.)
3. `GET /reservations/{id}/guest-session-id` (owner) → `guestSessionId`; call it twice, the id must be the same.
4. `GET /guest/groups/{groupId}/sessions` (owner) → check: 4 participants (owner + A, B, C);
   one with `type == "Owner"` and `id == guestSessionId`; `total` == sum of participant totals.
5. Same GET **with a second, freshly registered customer's token** → 403.
6. `DELETE /guest/groups/{groupId}/sessions/{C}` (owner) → 200, C is gone, totals recalculated.
7. `DELETE` of the owner's own session → 400.
8. `POST /group-reservations/{groupId}/submit` `{ reservationId }` (owner) → 200 and the body is
   exactly `[ {dishId: D1, quantity: 2} ]` (only A is submitted now; B is not).
9. The same submit again → 400.
10. `GET /orders/{reservationId}` (owner): the order contains D1 with quantity 1 (owner) + 2 (A) = 3
    if the owner's own dish was D1, otherwise D1 = 2 next to the owner's dish.
    (Check the response shape in `Handlers/Order/GetOrderByReservationHandler.cs`.)
11. Print a summary line `PASSED n/n` or `FAILED k of n` and exit 0/1.

**Done when**
- The script prints `PASSED` against the deployed API.
- If a step fails, the report quotes the step, the status and the body. **Do not change the backend
  to make it pass** — report it.

---

## Task 4 — AQA: API client for the group pre-order (small)

**Goal.** The AQA framework can call the four owner endpoints and the guest endpoints.

**Read first:** `aqa/restaurant-aqa-framework/CLAUDE.md` (applies to any AI assistant) and
`Api/OrdersApi.cs`, `Api/ApiClientBase.cs` as the pattern to copy.

**Files you may change**
- `aqa/restaurant-aqa-framework/Api/GroupPreOrderApi.cs` (new)
- `aqa/restaurant-aqa-framework/Api/Dtos/GroupPreOrders/*.cs` (new)

**Requirements**
- Methods: `GetSessionsAsync(groupId)`, `GetOwnerSessionIdAsync(reservationId)`,
  `SubmitAsync(groupId, reservationId)`, `DeleteSessionAsync(groupId, guestSessionId)`,
  `GetInviteTokenAsync(reservationId)`, `GetInviteAsync(token)`,
  `JoinAsGuestAsync(groupId, guestName)`, `AddGuestDishAsync(sessionId, sessionToken, dishId, quantity)`,
  `SubmitGuestCartAsync(sessionId, sessionToken)`.
- DTOs as C# records matching `docs/GroupPreOrder.md`; use the JSON options in `Api/JsonDefaults.cs`
  (they should already be case-insensitive — check).
- Follow the existing retry rule: POST is never retried.

**Done when**
- From `aqa/restaurant-aqa-framework`: `dotnet build` succeeds **with 0 warnings** (the CI treats warnings as errors).
- `dotnet format --verify-no-changes` passes.

---

## Task 5 — AQA: level 1 scenarios, "only the owner sees the group" (medium)

**Goal.** First Gherkin feature for Direction 1 of `homework-aqa.md`, API only.

**Files you may change**
- `aqa/restaurant-aqa-framework/Features/GroupPreOrder.feature` (new)
- `aqa/restaurant-aqa-framework/StepDefinitions/GroupPreOrderStepDefinitions.cs` (new)
- `aqa/restaurant-aqa-framework/Screenplay/Tasks/*GroupPreOrder*.cs`, `Screenplay/Questions/*Group*.cs` (new)

**Scenarios** (tag the feature `@api`; business language only — no URLs or status codes in Gherkin,
see CLAUDE.md "Writing Gherkin"):
1. A group nobody has joined yet → the owner sees no participants and a total of 0.
2. Another customer asks for the owner's group → is refused (forbidden).
3. The owner asks for a group that does not exist → is told it does not exist.

Set-up (customer, reservation, …) goes into Screenplay tasks that use Task 4's client and
`Api/Seeding/SampleDataFactory.cs`, and registers clean-up with `CleanupScope` (CLAUDE.md
"Test data"). Map "refused"/"does not exist" to status codes **only** in `OutcomeStepDefinitions`
(extend it if a phrase is missing — that file may be changed for this).

**Done when**
- `npx gherkin-lint -c .gherkin-lintrc "Features/*.feature"` passes.
- `dotnet build` has 0 warnings.
- With `.env` pointing to the deployment:
  `dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=api]"` → the 3 new scenarios pass.

---

## Task 6 — AQA: README of the solution (small)

**Goal.** The README that `homework-aqa.md` requires for Direction 1.

**Files you may change:** `aqa/restaurant-aqa-framework/README.md` (add a section at the top; keep the rest)

**Content**
- Chosen direction: **Direction 1 — group pre-order**, API tests + one minimal UI test (backend deployed).
- Approach: BDD (Reqnroll) + Screenplay, as in the existing framework.
- How to configure (`.env` keys used) and run all tests, only `@api`, only one feature.
- Where the reports are (`TestResults/`).
- "Behaviour not defined in the homework": copy the list from
  `backend/restaurant-backend-app/docs/GroupPreOrder.md` → "Behaviour chosen where the specification is silent"
  (link to it, do not paraphrase it differently).

**Done when** the section renders correctly on GitHub and every command in it was actually run once.
