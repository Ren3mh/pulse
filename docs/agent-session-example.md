# Example: a model answering from the marts

A real run of `claude-sonnet-5-5` through Claude Code, with **only** the pulse-marts MCP tools allowed.
Data is simulated (scripts/simulate_traffic.py). Reproduce with the command in prototype/README.md.

**Question:** Which product texts should we write first for the new German webshop, and what are customers searching for that they cannot find?

## What the model did

| # | Tool | Arguments | Outcome |
| --- | --- | --- | --- |
| 1 | `list_marts` | `{}` | 4 marts listed |
| 2 | `describe_mart` | `{"name": "mart_catalog_gaps"}` | 9 documented columns |
| 3 | `describe_mart` | `{"name": "mart_search_demand"}` | 7 documented columns |
| 4 | `query_mart` | `{"name": "mart_catalog_gaps", "query": "{\"filters\":[{\"column\":\"missing_german_text\",\"op\":\"=\",\"value\":true}],\"order_by\":\"priority\",\…` | **Refused:** unknown argument `query` |
| 5 | `query_mart` | `{"name": "mart_search_demand", "query": "{\"filters\":[{\"column\":\"never_found_anything\",\"op\":\"=\",\"value\":true}],\"order_by\":\"searches d…` | **Refused:** unknown argument `query` |
| 6 | `ToolSearch` | `{"query": "select:mcp__pulse-marts__query_mart", "max_results": 1}` | Loaded the tool's full schema |
| 7 | `query_mart` | `{"name": "mart_catalog_gaps", "filters": [{"column": "missing_german_text", "op": "=", "value": true}], "order_by": "priority", "descending": false…` | 5 rows from mart_catalog_gaps |
| 8 | `query_mart` | `{"name": "mart_search_demand", "filters": [{"column": "never_found_anything", "op": "=", "value": true}], "order_by": "searches", "limit": 10}` | 7 rows from mart_search_demand |

Steps 4 and 5 are the interesting ones. The model guessed the arguments, packing its
filters into a `query` string. The server refused the unknown argument instead of
silently ignoring it, so the model loaded the real schema and retried. The filtering
then happened server-side, inside the controlled layer.

## The model's answer

I've pulled both marts and am writing up the answer now.

**Marts used:** `mart_catalog_gaps` and `mart_search_demand`.

**Write German texts first, in this order (by views):**
1. **Mose Dining Table** has 122 views. Only 4.8% of viewers add it to the cart, and the Danish text is missing too.
2. **Kyst Table Lamp** has 98 views and a 21% add-to-cart rate.
3. **Vadehav Sofa, 2 seats** has 86 views, a 5.1% add-to-cart rate, and no Danish text either.
4. **Fjord Ottoman** has 50 views.
5. **Saltholm Bench** has 27 views, but 42% of viewers add it to the cart. It also has no dimensions.

Only five products lack German text. The mart's priority 3 is a product that isn't missing German text, so I left it out.

**Searches that never returned anything** (every search returned zero products):
- "outdoor": 26 searches
- "egg chair": 25
- "oak": 21
- "sofabord" (Danish for coffee table): 15
- "wool": 10
- "rattan": 8
- "garden furniture": 5

**What this suggests:**
- Customers want outdoor and garden furniture and rattan, which the shop doesn't seem to offer.
- Material searches ("oak", "wool") find nothing, even though materials are probably in the catalogue. That points to a search-indexing problem, not missing stock.
- "Sofabord" shows search doesn't handle Danish synonyms. Expect the same problem with German terms on the new webshop.

The search marts only list terms searched by at least 5 different visitors, so rarer gaps aren't visible.

*Cost of this run: $0.053.*
