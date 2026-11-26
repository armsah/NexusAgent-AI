import re
import unicodedata


_PATTERNS = tuple(re.compile(pattern, re.IGNORECASE | re.DOTALL)
    for pattern in (
        r"\b(ignore|disregard|override|forget|bypass)\b.{0,100}\b(previous|prior|system|developer|instructions|rules|policy)\b",
        r"\b(system|developer)\s*(message|prompt|instructions)\s*:",
        r"\b(reveal|print|expose|leak|disclose)\b.{0,100}\b(system prompt|api key|secrets?|credentials)\b",
        r"\b(execute|run)\b.{0,60}\b(shell|command|powershell|bash|script)\b",
        r"\b(send|upload|exfiltrate)\b.{0,100}\b(data|documents|secrets?|credentials)\b",
        r"\b(you are now|act as|pretend to be)\b.{0,80}\b(system|developer|administrator|assistant)\b",
        r"\[\s*(im_start|im_end|/?inst)\s*\]",
        r"<\s*\|(im_start|im_end|system|assistant|developer)\|\s*>",
    )
)


def is_suspicious(content: str) -> bool:
    normalized = unicodedata.normalize("NFKC", content)
    normalized = "".join(
        char for char in normalized
        if unicodedata.category(char) != "Cf"
    )
    normalized = " ".join(normalized.split())

    return any(pattern.search(normalized) for pattern in _PATTERNS)