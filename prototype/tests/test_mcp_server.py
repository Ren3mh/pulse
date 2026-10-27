"""The MCP server as an agent sees it: tool list, schemas and error messages.

These call the server in-process, so they need no database for the error paths.
"""

import asyncio

import pytest

from pulse_mcp.server import server


def call(name, arguments):
    return asyncio.run(server.call_tool(name, arguments))


def test_offers_three_read_only_tools():
    tools = asyncio.run(server.list_tools())
    assert [t.name for t in tools] == ["list_marts", "describe_mart", "query_mart"]
    assert all(t.annotations.read_only_hint for t in tools)


def test_schemas_refuse_unknown_arguments():
    for tool in asyncio.run(server.list_tools()):
        assert tool.input_schema["additionalProperties"] is False


def test_unknown_argument_is_an_error_not_ignored():
    # The mistake a real model made: filters packed into a "query" string.
    with pytest.raises(Exception, match="query"):
        call("query_mart", {"name": "mart_search_demand", "query": '{"filters": []}'})


def test_bad_operator_is_explained():
    with pytest.raises(Exception, match="includes"):
        call("query_mart", {"name": "mart_search_demand",
                            "filters": [{"column": "searches", "op": "LIKE", "value": 1}]})
