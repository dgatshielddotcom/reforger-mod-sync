# Server fixture

`server-detail.json` is a reduced public ReforgerMods server response observed on 2026-10-09. It contains the publicly reported server identity, address, timestamps, and 193 mod IDs/names/versions used by the provider tests. Session IDs, direct join codes, analytics, media URLs, and unrelated fields were removed.

The tests refresh only its observation timestamps in memory, then mutate copies to exercise invalid responses. It is test data, not a current mod list for cleanup.

Source: https://api.reforgermods.net/v2/servers/18131282-bda6-46b9-b135-f654ee5507b1

