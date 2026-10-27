# pulse prototype: architecture

How the Python prototype in `prototype/` fits together: the event API, the dbt marts and the MCP server that gives an AI agent controlled access to them. The diagrams are
[Mermaid](https://mermaid.js.org/); GitHub draws them, and they can be edited as text.

## Contents

1. [What happens when someone clicks a product](#1-what-happens-when-someone-clicks-a-product)
2. [Which file talks to which](#2-which-file-talks-to-which)
3. [The Event model and the modules around it](#3-the-event-model-and-the-modules-around-it)
4. [The two tables](#4-the-two-tables)
5. [Startup, and how the tests reuse it](#5-startup-and-how-the-tests-reuse-it)
6. [Controlled context: what the model can see](#6-controlled-context-what-the-model-can-see)
7. [One agent question, and every check on the way](#7-one-agent-question-and-every-check-on-the-way)
8. [Where the next phases plug in](#8-where-the-next-phases-plug-in)

## 1. What happens when someone clicks a product

*UML sequence diagram.* Follow one click from the shop to a row in Postgres. Read top to bottom: each arrow is a call, dashed arrows are answers.

```mermaid
sequenceDiagram
    actor U as Shopper
    participant S as index.html
    participant T as track.js
    participant A as app.py
    participant E as Event model
    participant St as store.py
    participant P as Postgres
    U->>S: clicks Fjord Lounge Chair
    S->>T: track("product_viewed", props)
    T->>T: add event_id, occurred_at, distinct_id
    T->>A: POST /events (JSON)
    A->>E: validate body
    alt invalid
        E-->>A: validation errors
        A-->>T: 422 with reasons
        T->>T: console.warn, shop keeps working
    else valid
        E-->>A: Event object
        A->>St: save_event(event)
        St->>P: INSERT ... ON CONFLICT DO NOTHING
        P-->>St: 1 row (new) or 0 rows (duplicate)
        St-->>A: True or False
        A-->>T: 202 with event_id, duplicate
    end
    T-->>S: pulse:tracked, shown in Events sent
```

- **track.js** builds the whole event, including `event_id` and `occurred_at`. The server never invents those.
- **FastAPI** checks the JSON against the `Event` model before your code runs. Bad input never reaches `store.py`.
- **ON CONFLICT DO NOTHING** is what makes a retry safe: the same `event_id` twice gives one row and `"duplicate": true`.
- If anything fails, track.js only logs a warning. Analytics must never break the shop.

## 2. Which file talks to which

*Component diagram.* Arrows point from the file that calls to the file it depends on. Labels say what crosses the line.

```mermaid
flowchart TB
    subgraph Browser
        I["index.html (shop UI)"]
        T["track.js"]
    end
    subgraph API["API (Python, FastAPI)"]
        APP["app.py (routes)"]
        EV["events.py (Event model)"]
        ST["store.py (save_event)"]
        CA["catalog.py (product queries)"]
        DB["db.py (connect, schema, seed)"]
    end
    subgraph PG["Postgres"]
        EVT[("events")]
        PRD[("products")]
    end
    I -->|"track(...)"| T
    T -->|"POST /events"| APP
    I -->|"GET /products"| APP
    APP -->|"validates with"| EV
    APP -->|"save_event()"| ST
    APP -->|"list_products()"| CA
    ST -->|"uses"| EV
    ST -->|"connect()"| DB
    CA -->|"connect()"| DB
    ST -->|"INSERT"| EVT
    CA -->|"SELECT"| PRD
    DB -->|"schema.sql"| EVT
    DB -->|"seed_products.sql"| PRD
```

- **app.py** is the only file that knows about HTTP. The other modules are plain Python functions you can test or reuse without a server.
- **db.py** is the only file that knows where Postgres is. Change `DATABASE_URL` and everything follows, which is how the tests use `pulse_test`.
- The browser only sees three URLs: `/shop/`, `/products` and `/events`.

## 3. The Event model and the modules around it

*UML class diagram.* Event is the one real class. The other boxes are modules, drawn as classes with a «module» stereotype so you can see their public functions.

```mermaid
classDiagram
    class Event {
        +int version = 1
        +UUID event_id
        +str event_name
        +str distinct_id
        +datetime occurred_at
        +dict properties
        event_name_is_object_verb()
        occurred_at_has_timezone()
        property_keys_are_snake_case()
    }
    class app {
        <<module>>
        +health() dict
        +receive_event(Event) dict
        +products(q) list
        +product(product_id) dict
    }
    class store {
        <<module>>
        +save_event(Event) bool
    }
    class catalog {
        <<module>>
        +list_products(query) list
        +get_product(product_id) dict
    }
    class db {
        <<module>>
        +connect() Connection
        +apply_schema()
        +seed_products()
    }
    app ..> Event : validates request body
    app ..> store : writes events
    app ..> catalog : reads products
    app ..> db : runs at startup
    store ..> Event : stores
    store ..> db : connect()
    catalog ..> db : connect()
```

- All validation lives in **Event**: the three validators enforce the rules from `docs/event-format.md`.
- `extra="forbid"` is why a client that sends `received_at` gets a 422.
- Dashed arrows are «uses» dependencies, not inheritance. Nothing inherits from anything here.

## 4. The two tables

*ER diagram.* events is append-only and grows with every click. products is small and changes rarely.

```mermaid
erDiagram
    EVENTS {
        uuid event_id PK "client-generated"
        smallint version
        text event_name "object_verb"
        text distinct_id "anonymous id"
        timestamptz occurred_at "client clock"
        timestamptz received_at "server clock"
        jsonb properties "flat keys"
    }
    PRODUCTS {
        text product_id PK "slug"
        text name
        text designer
        text category
        numeric price_dkk
        text_array materials
        int width_cm "nullable"
        int depth_cm "nullable"
        int height_cm "nullable"
        text description_da "nullable"
        text description_de "often missing"
    }
    PRODUCTS ||..o{ EVENTS : "named in properties.product_id"
```

- The dotted line is a **soft link**: events point at products through `properties.product_id` inside the JSON, not a foreign key. That keeps the event format generic, so any event can carry any properties.
- The nullable columns in products are deliberate gaps: 5 products lack German text and 2 lack width. A future `catalog_gaps` MCP tool will find them.
- Two indexes match the two questions asked most: “how many X between dates” and “what did this user do”.

## 5. Startup, and how the tests reuse it

*Activity diagram.* The app prepares its own database on every start. The tests start the same app, so they test the real startup too.

```mermaid
flowchart TD
    U["uvicorn pulse.app:app"] --> L["lifespan starts"]
    PT["pytest"] --> C["conftest.py sets DATABASE_URL to pulse_test"]
    C --> TC["TestClient enters the app"]
    TC --> L
    L --> S["db.apply_schema(): CREATE TABLE IF NOT EXISTS"]
    S --> SD["db.seed_products(): INSERT ... ON CONFLICT DO NOTHING"]
    SD --> R{"started by?"}
    R -->|"uvicorn"| SV["serve requests on :8000"]
    R -->|"pytest"| TR["TRUNCATE events"]
    TR --> TT["run one test, then repeat"]
```

- Both SQL files are written to be **safe to run again**: `CREATE TABLE IF NOT EXISTS` and `ON CONFLICT DO NOTHING`. That's why restarting never fails or duplicates products.
- Tests talk to a real Postgres database, `pulse_test`, instead of mocks. Slower by a second, but they catch SQL mistakes mocks would hide.

## 6. Controlled context: what the model can see

*Data flow diagram.* Three Postgres schemas: raw tables, staging views and documented mart tables, all built by dbt except raw. Raw events flow up into a few documented marts. The agent only reaches the top layer, and gets its context from dbt's own documentation.

```mermaid
flowchart TB
    subgraph raw["RAW"]
        EV[("events (visitor ids)")]
        PR[("products")]
    end
    subgraph stg["STAGING"]
        SE["stg_events"]
        SP["stg_products"]
    end
    subgraph marts["MARTS"]
        M1["mart_product_performance"]
        M2["mart_search_demand (5+ visitors per term)"]
        M3["mart_catalog_gaps"]
        M4["mart_daily_funnel"]
    end
    EV --> SE
    PR --> SP
    SE --> M1
    SE --> M2
    SE --> M4
    SP --> M1
    M1 --> M3
    SP --> M3
    MAN[/"manifest.json (descriptions, grain, agent_exposed)"/]
    MCP["pulse_mcp server, role pulse_agent"]
    AG["AI agent"]
    MAN -->|"context"| MCP
    marts -->|"SELECT, read-only"| MCP
    MCP -->|"list_marts, describe_mart, query_mart"| AG
    AG -.-x|"permission denied"| raw
```

- **Raw and staging** hold one row per click with visitor ids. The `pulse_agent` role gets *permission denied* on both.
- **Marts** answer questions ahead of time and contain no visitor ids. `mart_search_demand` drops any search term typed by fewer than 5 visitors, so rare or personal searches never reach the model.
- **manifest.json** is written by `dbt build`. The MCP server reads each mart's description, grain and column descriptions from it, so the model's context and the SQL are kept in one place and can't drift apart.
- Only models marked `agent_exposed: true` and only documented columns are offered. A new mart is invisible to agents until someone documents it and opts it in.

## 7. One agent question, and every check on the way

*UML sequence diagram.* What happens when the model calls query_mart. Each check can stop the call, and each refusal says how to fix it.

```mermaid
sequenceDiagram
    participant AG as AI agent
    participant S as server.py
    participant M as marts.py
    participant PG as Postgres (pulse_agent)
    AG->>S: query_mart(name, columns, filters, order_by, limit)
    S->>S: arguments match schema? unknown ones refused
    S->>M: get_mart(name)
    M->>M: exposed mart in manifest?
    M->>M: columns, filters, order_by documented?
    M->>M: build SQL with Identifier and parameters, cap at 100 rows
    M->>PG: SELECT ... FROM marts.mart_x (read-only transaction)
    alt anything outside marts
        PG-->>M: permission denied
    else
        PG-->>M: rows
    end
    M-->>S: rows, row_count, truncated
    S-->>AG: result, or a refusal that says how to fix the call
```

- **Unknown arguments are refused**, not ignored. A real model packed its filters into a made-up `query` argument; silently ignoring it would have returned unfiltered rows that looked filtered. See `docs/agent-session-example.md`.
- Mart and column names are checked against the manifest, then quoted with `sql.Identifier`. Values are always query parameters, so there is no way to inject SQL.
- Even if every check above failed, Postgres would still refuse: the role can only SELECT on marts, every transaction is read-only, and queries stop after 5 seconds.

## 8. Where the next phases plug in

*Roadmap diagram.* Solid lines exist today. Dashed lines are planned. Note how much of the future hangs off store.py and the marts.

```mermaid
flowchart TB
    T["track.js"] --> APP["app.py"]
    APP --> ST["store.py"]
    ST -->|"today"| PG[("Postgres raw tables")]
    ST -.->|"phase 4"| Q[["queue"]]
    Q -.-> W["worker"]
    W -.-> PG
    PG -->|"dbt build"| MARTS["dbt marts"]
    MARTS --> MCP["MCP server (read-only)"]
    MCP --> AG["AI agent"]
    MARTS -.->|"later"| D["dashboard"]
```

- **Phase 4** swaps one line of responsibility: `save_event()` publishes to a queue instead of writing to Postgres. track.js, app.py and the tests don't change.
- **A dashboard** can read the same marts as the agent, so people and the model see the same numbers.
- **More marts** are the main way to give an agent new abilities: add a model, document it, mark it `agent_exposed`.
