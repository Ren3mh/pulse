-- How each product does: attention (views) and intent (cart adds), all time.
-- Every product appears, also those nobody has looked at yet.

with views as (
    select
        product_id,
        count(*)                    as views,
        count(distinct distinct_id) as unique_viewers
    from {{ ref('stg_events') }}
    where event_name = 'product_viewed'
    group by product_id
),

cart_adds as (
    select
        product_id,
        count(*)                    as cart_adds,
        count(distinct distinct_id) as unique_cart_adders,
        sum(price_dkk)              as cart_value_dkk
    from {{ ref('stg_events') }}
    where event_name = 'cart_item_added'
    group by product_id
)

select
    p.product_id,
    p.name,
    p.designer,
    p.category,
    p.price_dkk,
    p.description_da is not null                  as has_description_da,
    p.description_de is not null                  as has_description_de,
    coalesce(v.views, 0)                          as views,
    coalesce(v.unique_viewers, 0)                 as unique_viewers,
    coalesce(c.cart_adds, 0)                      as cart_adds,
    coalesce(c.unique_cart_adders, 0)             as unique_cart_adders,
    round(
        coalesce(c.unique_cart_adders, 0)::numeric / nullif(v.unique_viewers, 0), 3
    )                                             as view_to_cart_rate,
    coalesce(c.cart_value_dkk, 0)                 as cart_value_dkk
from {{ ref('stg_products') }} as p
left join views     as v using (product_id)
left join cart_adds as c using (product_id)
