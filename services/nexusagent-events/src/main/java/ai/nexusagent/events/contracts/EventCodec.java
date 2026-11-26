package ai.nexusagent.events.contracts;

import com.fasterxml.jackson.core.JsonFactory;
import com.fasterxml.jackson.core.StreamReadConstraints;
import com.fasterxml.jackson.core.StreamReadFeature;
import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.MapperFeature;
import com.fasterxml.jackson.databind.SerializationFeature;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.json.JsonMapper;
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule;
import org.springframework.stereotype.Component;

import java.nio.charset.StandardCharsets;

@Component
public final class EventCodec {
    private static final int MAX_BYTES = 16_384;

    private final ObjectMapper mapper = JsonMapper.builder(
        JsonFactory.builder()
            .enable(StreamReadFeature.STRICT_DUPLICATE_DETECTION)
            .streamReadConstraints(StreamReadConstraints.builder()
                .maxNestingDepth(8)
                .maxStringLength(4096)
                .maxNumberLength(32)
                .build())
            .build())
        .addModule(new JavaTimeModule())
        .enable(DeserializationFeature.FAIL_ON_UNKNOWN_PROPERTIES)
        .enable(DeserializationFeature.FAIL_ON_NULL_FOR_PRIMITIVES)
        .enable(DeserializationFeature.FAIL_ON_TRAILING_TOKENS)
        .enable(DeserializationFeature.FAIL_ON_NUMBERS_FOR_ENUMS)
        .disable(DeserializationFeature.ACCEPT_FLOAT_AS_INT)
        .disable(MapperFeature.ALLOW_COERCION_OF_SCALARS)
        .disable(SerializationFeature.WRITE_DATES_AS_TIMESTAMPS)
        .build();

    public DocumentEvent readDocument(String json) {
        return read(json, DocumentEvent.class);
    }

    public WorkflowEvent readWorkflow(String json) {
        return read(json, WorkflowEvent.class);
    }

    public String writeDocument(DocumentEvent event) {
        return write(event);
    }

    public String writeWorkflow(WorkflowEvent event) {
        return write(event);
    }

    private <T> T read(String json, Class<T> type) {
        if (json == null || json.length() > MAX_BYTES
                || json.getBytes(StandardCharsets.UTF_8).length > MAX_BYTES) {
            throw new IllegalArgumentException("Invalid event size");
        }

        try {
            T event = mapper.readValue(json, type);
            if (event == null) {
                throw new IllegalArgumentException("Event is required");
            }
            return event;
        } catch (JsonProcessingException exception) {
            throw new IllegalArgumentException("Invalid event contract");
        }
    }

    private String write(Object event) {
        if (event == null) {
            throw new IllegalArgumentException("Event is required");
        }
        try {
            return mapper.writeValueAsString(event);
        } catch (JsonProcessingException exception) {
            throw new IllegalArgumentException("Event serialization failed");
        }
    }
}