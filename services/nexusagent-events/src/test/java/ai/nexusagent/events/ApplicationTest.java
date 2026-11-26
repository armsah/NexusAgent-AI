package ai.nexusagent.events;

import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.context.ApplicationContext;
import org.springframework.core.env.Environment;

import static org.junit.jupiter.api.Assertions.*;

@SpringBootTest
class ApplicationTest {
    @Autowired
    ApplicationContext context;

    @Autowired
    Environment environment;

    @Test
    void loadsServiceContext() {
        assertNotNull(context.getBean(EventProcessingApplication.class));
    }

    @Test
    void doesNotExposeAnUnauthenticatedWebServer() {
        assertEquals("none",
            environment.getProperty("spring.main.web-application-type"));
    }
}