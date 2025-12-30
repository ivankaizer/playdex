# DB Tests

## Where queries live

- DB query implementations: `src/infrastructure/db/queries.ts`
- DB test cases (names, seed SQL, and expectations): `tests/db/cases.ts`

## Print seed SQL for a db test

Use the dump script with the test name:

```sh
docker exec -it playdex-app-1 bun run db:dump "calculate total play sessions"
```

## Run all db tests

```sh
docker exec -e DATABASE_URL=postgresql://playdex:playdex@db:5432/playdex_test_db -it playdex-app-1 bun run db:test
```

## Run a specific db test

Filter by test name:

```sh
docker exec -e DATABASE_URL=postgresql://playdex:playdex@db:5432/playdex_test_db -it playdex-app-1 bun run db:test -- --test-name-pattern "calculate total play sessions"
```
