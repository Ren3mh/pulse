def test_lists_seeded_products(client):
    products = client.get("/products").json()

    assert len(products) == 12
    assert {"product_id", "name", "designer", "price_dkk", "description_de"} <= products[0].keys()


def test_search_matches_name_designer_and_category(client):
    assert {p["product_id"] for p in client.get("/products?q=kyst").json()} == {
        "kyst-pendant",
        "kyst-table-lamp",
    }
    assert len(client.get("/products?q=signe dahl").json()) == 3
    assert len(client.get("/products?q=lighting").json()) == 3


def test_get_one_product(client):
    product = client.get("/products/fjord-lounge-chair").json()

    assert product["name"] == "Fjord Lounge Chair"
    assert product["materials"] == ["oak", "wool"]
    assert product["price_dkk"] == 18995


def test_unknown_product_is_404(client):
    assert client.get("/products/no-such-thing").status_code == 404


def test_catalogue_has_deliberate_gaps(client):
    # The seed data leaves gaps on purpose, for the later catalog_gaps MCP tool.
    products = client.get("/products").json()

    assert sum(p["description_de"] is None for p in products) == 5
    assert sum(p["width_cm"] is None for p in products) == 2


def test_shop_page_is_served(client):
    response = client.get("/shop/")

    assert response.status_code == 200
    assert "NORDLYS DESIGN" in response.text
