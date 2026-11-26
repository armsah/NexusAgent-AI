from fastapi import APIRouter, Depends, HTTPException
from app.inference_auth import require_inference_auth

from app.ollama import InferenceError
from app.security import is_suspicious
from app.retrieval import (
    EmbeddingRequest,
    EmbeddingResponse,
    GroundedRequest,
    GroundedResponse,
    RetrievalClient,
)


router = APIRouter(dependencies=[Depends(require_inference_auth)])
retrieval = RetrievalClient()


@router.post("/v1/embed", response_model=EmbeddingResponse)
async def embed(request: EmbeddingRequest):
    if not request.text.strip():
        raise HTTPException(422, "Text cannot be blank")

    try:
        return await retrieval.embed(request)
    except InferenceError:
        raise HTTPException(503, "Embedding unavailable") from None


@router.post("/v1/grounded", response_model=GroundedResponse)
async def grounded(request: GroundedRequest):
    if not request.question.strip():
        raise HTTPException(422, "Question cannot be blank")

    if is_suspicious(request.question):
        raise HTTPException(400, "Unsafe grounded question")

    try:
        return await retrieval.grounded(request)
    except ValueError:
        raise HTTPException(422, "Invalid evidence") from None
    except InferenceError:
        raise HTTPException(503, "Grounded inference unavailable") from None