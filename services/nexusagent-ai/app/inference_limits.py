import asyncio
import json

from starlette.responses import JSONResponse


class InferenceLimitsMiddleware:
    """Bound request buffering, active inference and execution duration."""

    def __init__(
        self,
        app,
        max_body_bytes=262144,
        max_concurrent=2,
        timeout_seconds=125,
        body_timeout_seconds=5,
    ):
        self.app = app
        self.max_body_bytes = max_body_bytes
        self.max_concurrent = max_concurrent
        self.timeout_seconds = timeout_seconds
        self.body_timeout_seconds = body_timeout_seconds
        self.active = 0

    async def __call__(self, scope, receive, send):
        if (
            scope["type"] != "http"
            or not scope["path"].startswith("/v1/")
        ):
            await self.app(scope, receive, send)
            return

        async def reject(status, detail):
            await JSONResponse(
                {"detail": detail}, status_code=status
            )(scope, receive, send)

        if self.active >= self.max_concurrent:
            await reject(429, "Inference capacity exhausted")
            return

        self.active += 1

        try:
            lengths = [
                value for name, value in scope["headers"]
                if name.lower() == b"content-length"
            ]

            if len(lengths) > 1:
                await reject(400, "Invalid content length")
                return

            if lengths:
                try:
                    declared = int(lengths[0])
                    if declared < 0:
                        raise ValueError()
                except ValueError:
                    await reject(400, "Invalid content length")
                    return

                if declared > self.max_body_bytes:
                    await reject(413, "Request too large")
                    return

            body = bytearray()

            try:
                async with asyncio.timeout(self.body_timeout_seconds):
                    while True:
                        message = await receive()

                        if message["type"] == "http.disconnect":
                            return

                        chunk = message.get("body", b"")

                        if len(body) + len(chunk) > self.max_body_bytes:
                            await reject(413, "Request too large")
                            return

                        body.extend(chunk)

                        if not message.get("more_body", False):
                            break

            except TimeoutError:
                await reject(408, "Request body deadline exceeded")
                return

            if lengths and declared != len(body):
                await reject(400, "Invalid content length")
                return

            delivered = False
            started = False

            async def replay():
                nonlocal delivered

                if not delivered:
                    delivered = True
                    return {
                        "type": "http.request",
                        "body": bytes(body),
                        "more_body": False,
                    }

                return await receive()

            async def tracked_send(message):
                nonlocal started

                if message["type"] == "http.response.start":
                    started = True

                await send(message)

            try:
                async with asyncio.timeout(self.timeout_seconds):
                    await self.app(scope, replay, tracked_send)

            except TimeoutError:
                if started:
                    raise

                await reject(504, "Inference deadline exceeded")

        finally:
            self.active -= 1