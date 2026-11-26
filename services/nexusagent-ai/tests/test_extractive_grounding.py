import pytest

from app.retrieval import (
    Evidence,
    GroundedResponse,
    normalize_grounded_answer,
)


@pytest.mark.parametrize("answer, citations, accepted", [
    ("NexusAgent uses PostgreSQL.", ["S1"], True),
    ("NexusAgent uses PostgreSQL [S1].", ["S1"], True),
    ("NexusAgent does not use PostgreSQL.", ["S1"], False),
    ("PostgreSQL uses NexusAgent.", ["S1"], False),
    ("NexusAgent uses PostgreSQL [S2].", ["S1"], False),
    ("NexusAgent uses PostgreSQL.", ["S1", "S1"], False),
    ("NexusAgent uses PostgreSQL.", ["S9"], False),
    ("NexusAgent uses PostgreSQL.", [], False),
])
def test_extractive_citation_normalization(answer, citations, accepted):
    result = normalize_grounded_answer(
        GroundedResponse(
            answer=answer,
            cited_source_ids=citations,
        ),
        [Evidence(
            source_id="S1",
            excerpt="NexusAgent uses PostgreSQL.",
        )],
    )

    assert bool(result.cited_source_ids) == accepted

    if accepted:
        assert result.answer == "NexusAgent uses PostgreSQL [S1]."
    else:
        assert result.answer == "Insufficient evidence."