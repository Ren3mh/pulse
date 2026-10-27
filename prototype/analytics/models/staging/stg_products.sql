select
    product_id,
    name,
    designer,
    category,
    price_dkk,
    materials,
    width_cm,
    depth_cm,
    height_cm,
    description_da,
    description_de
from {{ source('pulse', 'products') }}
