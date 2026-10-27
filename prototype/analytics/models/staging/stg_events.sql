-- Events with the properties the marts need pulled out into typed columns.
-- Dates are in the shop's own timezone, so "a day" means a Danish day.

select
    event_id,
    event_name,
    distinct_id,
    occurred_at,
    received_at,
    (occurred_at at time zone 'Europe/Copenhagen')::date as event_date,
    properties ->> 'product_id'                        as product_id,
    lower(trim(properties ->> 'query'))                as search_query,
    (properties ->> 'results_count')::int              as results_count,
    (properties ->> 'price_dkk')::numeric(10, 2)       as price_dkk
from {{ source('pulse', 'events') }}
where version = 1
