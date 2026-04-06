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
