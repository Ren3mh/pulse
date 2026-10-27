-- A funnel can only narrow: nobody adds to the cart without viewing a product,
-- and nobody views a product without being a visitor. Returns the broken days.

select *
from {{ ref('mart_daily_funnel') }}
where product_viewers > visitors
   or searchers > visitors
   or cart_adders > product_viewers
