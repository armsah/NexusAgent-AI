from pydantic import BaseModel, ConfigDict, Field


class GenerationRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    prompt: str = Field(min_length=1, max_length=16000)
    max_tokens: int = Field(default=512, ge=1, le=2048)


class GenerationResponse(BaseModel):
    model: str
    answer: str