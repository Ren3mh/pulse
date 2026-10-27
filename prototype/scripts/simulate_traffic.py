"""Fill the events table with four weeks of simulated shop traffic.

Fictional visitors (distinct_id starts with "sim-") browse Nordlys Design:
they land on the shop, sometimes search, view products and sometimes add one
to the cart. Every event goes through the real Event model, so it obeys the
v1 format. Rerunning replaces earlier simulated events and gives the same data
(fixed random seed).

Two patterns are planted on purpose, so the marts have something to find:
- shoppers search for things the shop doesn't sell or can't find
  (search ignores materials, so "oak" finds nothing);
- products without a Danish description convert about half as well.

Usage: .venv/bin/python -m scripts.simulate_traffic [--days 28] [--end 2026-10-25]
"""

import argparse
import random
import uuid
from datetime import date, datetime, timedelta
from zoneinfo import ZoneInfo

from psycopg.types.json import Jsonb

from pulse import catalog, db
from pulse.events import Event

SHOP_TZ = ZoneInfo("Europe/Copenhagen")

# How often each product gets looked at, relative to the others.
POPULARITY = {
    "vadehav-sofa-3": 10, "fjord-lounge-chair": 9, "kyst-pendant": 8,
    "saltholm-dining-chair": 7, "mose-coffee-table": 6, "vadehav-sofa-2": 5,
    "mose-dining-table": 5, "kyst-table-lamp": 4, "lyng-floor-lamp": 4,
    "fjord-ottoman": 3, "saltholm-bench": 2, "skaer-vase": 3,
}

# What people type, and how often. Some find nothing in this catalogue.
SEARCHES = {
    "sofa": 9, "lounge chair": 7, "lamp": 6, "kyst": 5, "dining table": 5,
    "egg chair": 6, "oak": 5, "outdoor": 4, "rattan": 3, "pendant": 3,
    "signe dahl": 2, "vase": 2, "garden furniture": 2, "wool": 2,
    "sofabord": 3,  # Danish for coffee table; product names are English
}

# One-off searches: typos and very specific wishes. Each is typed by one or two
# visitors, so the search mart's privacy threshold keeps them out.
RARE_SEARCHES = [
    "lounge chiar", "kyst pendel messing", "sofa 3 pers grå", "fjordd", "vadehav hynder",
    "bord 200x95 eg", "lampe til børneværelse", "skær vase blå", "saltholm stol sort",
    "mose sofabord rundt", "ottoman uld", "lyng lampe hør",
]


def event(name: str, visitor: str, at: datetime, **properties) -> Event:
    return Event(
        version=1,
        event_id=uuid.uuid4(),
        event_name=name,
        distinct_id=visitor,
        occurred_at=at,
        properties=properties,
    )


def simulate_visit(rng: random.Random, visitor: str, start: datetime, products: dict) -> list[Event]:
    at = start
    events = [event("page_viewed", visitor, at, page="/shop/")]

    candidates = list(products)
    if rng.random() < 0.45:
        if rng.random() < 0.05:
            query = rng.choice(RARE_SEARCHES)
        else:
            query = rng.choices(list(SEARCHES), weights=list(SEARCHES.values()))[0]
        found = catalog.list_products(query)
        at += timedelta(seconds=rng.randint(5, 40))
        events.append(event("search_submitted", visitor, at, query=query, results_count=len(found)))
        if found:
            candidates = [p["product_id"] for p in found]
        elif rng.random() < 0.6:
            return events  # found nothing and left

    for _ in range(rng.choice([1, 1, 2, 2, 3, 4])):
        weights = [POPULARITY[p] for p in candidates]
        product = products[rng.choices(candidates, weights=weights)[0]]
        at += timedelta(seconds=rng.randint(10, 120))
        events.append(event("product_viewed", visitor, at,
                            product_id=product["product_id"], category=product["category"]))

        # Cheaper things go in the cart more often; missing texts hurt.
        chance = 0.22 if product["price_dkk"] < 5000 else 0.12 if product["price_dkk"] < 15000 else 0.07
        if product["description_da"] is None:
            chance *= 0.5
        if rng.random() < chance:
            at += timedelta(seconds=rng.randint(15, 90))
            events.append(event("cart_item_added", visitor, at,
                                product_id=product["product_id"], price_dkk=product["price_dkk"]))
    return events


def simulate(days: int, end: date, seed: int = 7) -> list[tuple[Event, datetime]]:
    rng = random.Random(seed)
    products = {p["product_id"]: p for p in catalog.list_products()}
    rows = []
    for offset in range(days):
        day = end - timedelta(days=days - 1 - offset)
        visitors = rng.randint(14, 26) + (8 if day.weekday() >= 5 else 0)
        for _ in range(visitors):
            visitor = f"sim-{rng.getrandbits(32):08x}"
            hour = rng.choices(range(8, 23), weights=[1, 2, 2, 2, 3, 2, 2, 2, 3, 4, 5, 5, 4, 3, 2])[0]
            start = datetime(day.year, day.month, day.day, hour, rng.randint(0, 59),
                             rng.randint(0, 59), tzinfo=SHOP_TZ)
            for ev in simulate_visit(rng, visitor, start, products):
                # Normal network delay between click and arrival at the API.
                rows.append((ev, ev.occurred_at + timedelta(milliseconds=rng.randint(15, 400))))
    return rows


def run(days: int = 28, end: date = date(2026, 10, 25)) -> tuple[int, int]:
    """Replace simulated events in the database. Returns (events, visitors)."""
    db.apply_schema()
    db.seed_products()
    rows = simulate(days, end)
    with db.connect() as conn:
        conn.execute("DELETE FROM events WHERE distinct_id LIKE 'sim-%'")
        with conn.cursor() as cur:
            cur.executemany(
                """INSERT INTO events (event_id, version, event_name, distinct_id,
                                       occurred_at, received_at, properties)
                   VALUES (%s, %s, %s, %s, %s, %s, %s)""",
                [(e.event_id, e.version, e.event_name, e.distinct_id, e.occurred_at,
                  received, Jsonb(e.properties)) for e, received in rows],
            )
    return len(rows), len({e.distinct_id for e, _ in rows})


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--days", type=int, default=28)
    parser.add_argument("--end", type=date.fromisoformat, default=date(2026, 10, 25))
    args = parser.parse_args()
    events, visitors = run(args.days, args.end)
    print(f"Simulated {events} events from {visitors} visitors over {args.days} days.")


if __name__ == "__main__":
    main()
