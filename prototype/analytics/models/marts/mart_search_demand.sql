-- What people search for, and whether the shop had an answer.
--
-- Privacy rule: a search term only appears once at least
-- var('search_min_visitors') different visitors have typed it. Rare terms can
-- identify a person or contain things they typed by mistake (names, emails,
-- order numbers), so they never leave this model.

select
    search_query,
    count(*)                                         as searches,
    count(distinct distinct_id)                      as unique_searchers,
    count(*) filter (where results_count = 0)        as zero_result_searches,
    round(avg(results_count), 1)                     as avg_results,
    bool_and(results_count = 0)                      as never_found_anything,
    max(event_date)                                  as last_searched_on
from {{ ref('stg_events') }}
where event_name = 'search_submitted'
  and search_query <> ''
group by search_query
having count(distinct distinct_id) >= {{ var('search_min_visitors', 5) }}
