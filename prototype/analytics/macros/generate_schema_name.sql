{#
    By default dbt names schemas "<target schema>_<custom schema>", e.g. analytics_marts.
    This uses the custom schema name as-is, so the agent role can be granted
    one predictable schema: marts.
#}
{% macro generate_schema_name(custom_schema_name, node) -%}
    {%- if custom_schema_name is none -%}
        {{ target.schema }}
    {%- else -%}
        {{ custom_schema_name | trim }}
    {%- endif -%}
{%- endmacro %}
