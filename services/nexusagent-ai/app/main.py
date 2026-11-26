from fastapi import Depends, FastAPI, HTTPException
from app.inference_auth import require_inference_auth
from app.inference_limits import InferenceLimitsMiddleware

from app.contracts import GenerationRequest, GenerationResponse
from app.ollama import OllamaClient, InferenceError


app = FastAPI(
    title="NexusAgent AI Inference Service",
    version="0.1.0",
)

app.add_middleware(InferenceLimitsMiddleware)

inference = OllamaClient()


@app.get("/health/live")
async def liveness():
    return {"status": "healthy"}


@app.post("/v1/generate", response_model=GenerationResponse, dependencies=[Depends(require_inference_auth)])
async def generate(request: GenerationRequest):
    if not request.prompt.strip():
        raise HTTPException(422, "Prompt cannot be blank")

    try:
        return await inference.generate(request)
    except InferenceError:
        raise HTTPException(
            status_code=503,
            detail="Local inference unavailable",
        ) from None

from app.retrieval_api import router
app.include_router(router)
