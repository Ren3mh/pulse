"""The event format (v1) as code. Mirrors docs/event-format.md; change both together."""

import re
from datetime import datetime, timezone
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, field_validator

SNAKE_CASE = re.compile(r"^[a-z][a-z0-9]*(_[a-z0-9]+)*$")

# Property values must be flat: no nested objects or lists.
PropertyValue = str | int | float | bool | None


class Event(BaseModel):
    # Unknown fields are rejected, including received_at: the server sets that.
    model_config = ConfigDict(extra="forbid")

    version: Literal[1]
    event_id: UUID
    event_name: str = Field(min_length=3, max_length=100)
    distinct_id: str = Field(min_length=1, max_length=200)
    occurred_at: datetime
    properties: dict[str, PropertyValue] = Field(default_factory=dict)

    @field_validator("event_name")
    @classmethod
    def event_name_is_object_verb(cls, value: str) -> str:
        # object_verb in snake_case, e.g. product_viewed. At least one underscore.
        if not SNAKE_CASE.match(value) or "_" not in value:
            raise ValueError("event_name must be snake_case object_verb, e.g. product_viewed")
        return value

    @field_validator("occurred_at")
    @classmethod
    def occurred_at_has_timezone(cls, value: datetime) -> datetime:
        # A timestamp without a zone is ambiguous, so it is refused rather than guessed.
        if value.tzinfo is None:
            raise ValueError("occurred_at must include a timezone, e.g. 2026-10-26T13:00:00Z")
        return value.astimezone(timezone.utc)

    @field_validator("properties")
    @classmethod
    def property_keys_are_snake_case(cls, value: dict) -> dict:
        bad = [key for key in value if not SNAKE_CASE.match(key)]
        if bad:
            raise ValueError(f"property keys must be snake_case: {', '.join(bad)}")
        return value
