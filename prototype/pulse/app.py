"""HTTP API: health check, event intake, product catalogue, and the test shop."""

from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, HTTPException, status
from fastapi.responses import RedirectResponse
from fastapi.staticfiles import StaticFiles

from pulse import catalog, db
from pulse.events import Event
from pulse.store import save_event

SHOP_DIR = Path(__file__).resolve().parent.parent / "shop"


@asynccontextmanager
async def lifespan(app: FastAPI):
    db.apply_schema()
    db.seed_products()
    yield


app = FastAPI(title="pulse", lifespan=lifespan)


@app.get("/health")
def health():
    # Lets Docker, CI and later the load balancer check that the API is up.
    return {"status": "ok"}


@app.post("/events", status_code=status.HTTP_202_ACCEPTED)
def receive_event(event: Event):
    # FastAPI has already validated the body against Event; bad input gets a 422.
    stored = save_event(event)
    return {"event_id": str(event.event_id), "duplicate": not stored}


@app.get("/products")
def products(q: str | None = None):
    return catalog.list_products(q)


@app.get("/products/{product_id}")
def product(product_id: str):
    found = catalog.get_product(product_id)
    if found is None:
        raise HTTPException(status_code=404, detail="product not found")
    return found


@app.get("/")
def root():
    return RedirectResponse("/shop/")


app.mount("/shop", StaticFiles(directory=SHOP_DIR, html=True), name="shop")
