import asyncio

from app.contracts import GenerationRequest
from app.ollama import OllamaClient
from app.retrieval import (
    EmbeddingRequest,
    Evidence,
    GroundedRequest,
    RetrievalClient,
)


async def main():
    generation = await OllamaClient().generate(
        GenerationRequest(
            prompt="What is 2 + 2? Answer briefly.",
            max_tokens=32,
        )
    )

    print("Generation:", generation.model, generation.answer)

    client = RetrievalClient()

    embedding = await client.embed(
        EmbeddingRequest(text="NexusAgent workflow governance")
    )

    assert len(embedding.embedding) == 1536
    print("Embedding dimensions:", len(embedding.embedding))

    grounded = await client.grounded(
        GroundedRequest(
            question="What database does NexusAgent use?",
            evidence=[
                Evidence(
                    source_id="S1",
                    excerpt="NexusAgent uses PostgreSQL.",
                )
            ],
        )
    )

    print("Grounded answer:", grounded.model_dump_json())

    assert "postgresql" in grounded.answer.lower()
    assert "S1" in grounded.cited_source_ids

    print("LIVE INFERENCE SMOKE TEST PASSED")


if __name__ == "__main__":
    asyncio.run(main())