-- One row per day: how many visitors reached each step.
-- Each step counts distinct visitors, so a visitor who views five products is one viewer.

select
    event_date,
    count(distinct distinct_id)                                                as visitors,
    count(distinct distinct_id) filter (where event_name = 'search_submitted') as searchers,
    count(distinct distinct_id) filter (where event_name = 'product_viewed')   as product_viewers,
    count(distinct distinct_id) filter (where event_name = 'cart_item_added')  as cart_adders,
    round(
        (count(distinct distinct_id) filter (where event_name = 'cart_item_added'))::numeric
        / nullif(count(distinct distinct_id), 0), 3
    )                                                                          as visitor_to_cart_rate
from {{ ref('stg_events') }}
group by event_date
