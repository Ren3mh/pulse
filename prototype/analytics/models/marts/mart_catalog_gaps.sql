-- Products with missing catalogue data, most-viewed first.
-- A gap on a product nobody looks at can wait; a gap on a top seller cannot.

with gaps as (
    select
        product_id,
        name,
        category,
        array_remove(array[
            case when description_da is null then 'description_da' end,
            case when description_de is null then 'description_de' end,
            case when width_cm is null or depth_cm is null or height_cm is null then 'dimensions' end,
            case when cardinality(materials) = 0 then 'materials' end
        ], null) as missing_fields
    from {{ ref('stg_products') }}
)

select
    g.product_id,
    g.name,
    g.category,
    g.missing_fields,
    cardinality(g.missing_fields)                       as gap_count,
    'description_de' = any(g.missing_fields)            as missing_german_text,
    perf.views,
    perf.view_to_cart_rate,
    row_number() over (order by perf.views desc, cardinality(g.missing_fields) desc, g.product_id)
                                                        as priority
from gaps as g
join {{ ref('mart_product_performance') }} as perf using (product_id)
where cardinality(g.missing_fields) > 0
