import asyncio
import httpx

from app.contracts import GenerationRequest, GenerationResponse


class InferenceError(RuntimeError):
    pass


class OllamaClient:
    def __init__(self, model: str = "qwen2.5:3b"):
        self.model = model
        self.base_url = "http://127.0.0.1:11434"

    async def generate(
        self, request: GenerationRequest
    ) -> GenerationResponse:
        payload = {
            "model": self.model,
            "prompt": request.prompt,
            "stream": False,
            "options": {
                "temperature": 0,
                "num_predict": request.max_tokens,
            },
        }

        try:
            async with asyncio.timeout(120):
                async with httpx.AsyncClient(
                    timeout=120.0,
                    trust_env=False,
                ) as client:
                    response = await client.post(
                        f"{self.base_url}/api/generate",
                        json=payload,
                    )
                    response.raise_for_status()
                    data = response.json()

            answer = data["response"]

            if not isinstance(answer, str) or not answer.strip():
                raise ValueError("Empty inference response")

            if data.get("done") is not True or data.get("done_reason") == "length":
                raise ValueError("Incomplete inference response")

            return GenerationResponse(
                model=self.model,
                answer=answer,
            )

        except (TimeoutError, httpx.HTTPError, ValueError, KeyError, TypeError) as exc:
            raise InferenceError("Local inference failed") from exc