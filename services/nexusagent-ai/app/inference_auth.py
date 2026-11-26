import hmac
import os

from fastapi import HTTPException, Request


async def require_inference_auth(request: Request):
    expected = os.environ.get("NEXUS_INFERENCE_API_KEY", "")

    if len(expected) < 32:
        raise HTTPException(503, "Inference authentication unavailable")

    supplied = request.headers.getlist("x-inference-key")

    if len(supplied) != 1:
        raise HTTPException(401, "Unauthorized inference request")

    if not hmac.compare_digest(
        supplied[0].encode("utf-8"),
        expected.encode("utf-8"),
    ):
        raise HTTPException(401, "Unauthorized inference request")