window.studyHubLessonSidebar = window.studyHubLessonSidebar || {
    scrollToLesson(sidebarElementId, lessonId) {
        return new Promise((resolve) => {
            const runScroll = () => {
                const sidebar = document.getElementById(sidebarElementId);
                if (!sidebar || !lessonId) {
                    resolve(false);
                    return;
                }

                const target = sidebar.querySelector(`[data-lesson-anchor="${lessonId}"]`);
                if (!target) {
                    resolve(false);
                    return;
                }

                const accordion = target.closest(".module-accordion");
                if (accordion) {
                    centerInScrollableContainer(sidebar, accordion);
                } else {
                    centerInScrollableContainer(sidebar, target);
                }

                const moduleBody = target.closest(".module-accordion-body.scrollable");
                if (moduleBody) {
                    centerInScrollableContainer(moduleBody, target);
                }

                resolve(true);
            };

            window.requestAnimationFrame(() => {
                window.requestAnimationFrame(runScroll);
            });
        });
    },

    resolvePlayerPopoverPlacement(popoverElement, panelElement, controlsElement, videoAreaElement) {
        if (!popoverElement || !panelElement || !controlsElement) {
            return {
                direction: "down",
                maxHeight: 0
            };
        }

        const gap = 10;
        const viewportHeight = Math.max(window.innerHeight || 0, document.documentElement?.clientHeight || 0, 1);
        const popoverRect = popoverElement.getBoundingClientRect();
        const panelRect = panelElement.getBoundingClientRect();
        const controlsRect = controlsElement.getBoundingClientRect();
        const videoRect = videoAreaElement ? videoAreaElement.getBoundingClientRect() : null;
        const panelHeight = Math.max(panelRect.height || 0, panelElement.scrollHeight || 0);

        const safeTopBound = videoRect
            ? Math.max(videoRect.bottom + 6, 0)
            : 0;

        const belowAnchorTop = Math.max(popoverRect.bottom + gap, controlsRect.bottom + gap);
        const availableBelow = Math.max(0, viewportHeight - belowAnchorTop - 8);

        const aboveAnchorBottom = popoverRect.top - gap;
        const availableAbove = Math.max(0, aboveAnchorBottom - safeTopBound - 8);

        const comfortableMinHeight = Math.min(140, panelHeight || 140);
        let direction = "down";

        if (availableBelow < comfortableMinHeight &&
            availableAbove > availableBelow &&
            availableAbove >= 80) {
            direction = "up";
        }

        const maxHeight = Math.max(
            0,
            Math.floor(direction === "up" ? availableAbove : availableBelow));

        return {
            direction,
            maxHeight
        };
    }
};

function centerInScrollableContainer(container, element) {
    if (!container || !element) {
        return;
    }

    const containerRect = container.getBoundingClientRect();
    const elementRect = element.getBoundingClientRect();
    const nextScrollTop = container.scrollTop
        + (elementRect.top - containerRect.top)
        - (container.clientHeight / 2)
        + (element.clientHeight / 2);

    container.scrollTo({
        top: Math.max(0, nextScrollTop),
        behavior: "auto"
    });
}
