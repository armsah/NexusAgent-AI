import asyncio

import httpx
import pytest
from starlette.responses import JSONResponse

from app.inference_limits import InferenceLimitsMiddleware


async def echo(scope, receive, send):
    message = await receive()
    await JSONResponse(
        {"size": len(message["body"])}
    )(scope, receive, send)


def client_for(app):
    return httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app),
        base_url="http://test",
    )


@pytest.mark.asyncio
async def test_body_limit():
    app = InferenceLimitsMiddleware(echo, max_body_bytes=32)

    async with client_for(app) as client:
        assert (await client.post(
            "/v1/generate", content=b"x" * 33
        )).status_code == 413

        response = await client.post(
            "/v1/generate", content=b"x" * 32
        )

    assert response.status_code == 200
    assert response.json()["size"] == 32


@pytest.mark.asyncio
async def test_chunked_body_limit():
    app = InferenceLimitsMiddleware(echo, max_body_bytes=32)

    async def chunks():
        yield b"x" * 20
        yield b"x" * 20

    async with client_for(app) as client:
        response = await client.post(
            "/v1/generate", content=chunks()
        )

    assert response.status_code == 413


@pytest.mark.asyncio
async def test_concurrency_rejection_and_slot_recovery():
    entered = asyncio.Event()
    release = asyncio.Event()

    async def blocked(scope, receive, send):
        entered.set()
        await release.wait()
        await echo(scope, receive, send)

    app = InferenceLimitsMiddleware(
        blocked, max_concurrent=1
    )

    async with client_for(app) as client:
        first = asyncio.create_task(
            client.post("/v1/generate", content=b"first")
        )

        try:
            await asyncio.wait_for(entered.wait(), 2)

            second = await client.post(
                "/v1/embed", content=b"second"
            )

            assert second.status_code == 429

        finally:
            release.set()
            assert (await first).status_code == 200

        third = await client.post(
            "/v1/generate", content=b"third"
        )

    assert third.status_code == 200
    assert app.active == 0


@pytest.mark.asyncio
async def test_execution_deadline_releases_capacity():
    cancelled = asyncio.Event()

    async def slow(scope, receive, send):
        try:
            await asyncio.sleep(10)
        finally:
            cancelled.set()

    app = InferenceLimitsMiddleware(
        slow, timeout_seconds=0.05
    )

    async with client_for(app) as client:
        response = await client.post(
            "/v1/generate", content=b"test"
        )

    assert response.status_code == 504
    assert cancelled.is_set()
    assert app.active == 0


@pytest.mark.asyncio
async def test_slow_body_deadline():
    app = InferenceLimitsMiddleware(
        echo, body_timeout_seconds=0.05
    )

    async def chunks():
        yield b"first"
        await asyncio.sleep(10)
        yield b"second"

    async with client_for(app) as client:
        response = await client.post(
            "/v1/generate", content=chunks()
        )

    assert response.status_code == 408
    assert app.active == 0


@pytest.mark.asyncio
async def test_invalid_content_length():
    app = InferenceLimitsMiddleware(echo)

    async with client_for(app) as client:
        response = await client.post(
            "/v1/generate",
            content=b"x",
            headers={"Content-Length": "invalid"},
        )

    assert response.status_code == 400
    assert app.active == 0


@pytest.mark.asyncio
async def test_public_health_not_subject_to_inference_capacity():
    app = InferenceLimitsMiddleware(echo, max_concurrent=1)
    app.active = 1

    async with client_for(app) as client:
        response = await client.get("/health/live")

    assert response.status_code == 200