(() => {
  if (window.studyhubSyncContentScriptLoaded) {
    return;
  }

  window.studyhubSyncContentScriptLoaded = true;

  const DOWNLOAD_BLOCK_SELECTOR = "div.lnk-download[data-downid]";
  const DOWNLOAD_SECTION_LABEL_SELECTOR = "p";
  const DOWNLOAD_SECTION_LIST_SELECTOR = "ul";
  const BLOCK_LABEL_SELECTOR = "p, strong, span, h1, h2, h3, h4, h5, h6, summary, button";
  const LESSON_HEADER_SELECTOR =
    "h1, h2, h3, h4, h5, h6, strong, summary, button, " +
    '[class*="title"], [class*="titulo"], [class*="accordion"], ' +
    '[class*="panel-heading"], [class*="card-header"], [role="button"]';
  const LESSON_HEADER_MAX_LENGTH = 120;

  const SECTION_DEFINITIONS = Object.freeze({
    videoDownload: {
      kind: "video-download",
      label: "Video para download",
      patterns: [/videos?\s+para\s+download/i, /video\s+para\s+download/i],
    },
    pratica: {
      kind: "pratica",
      label: "Aula Pratica",
      patterns: [/aula\s+pratica/i, /\bpratica\b/i],
    },
    teorica: {
      kind: "teorica",
      label: "Aula Teorica",
      patterns: [/aula\s+teorica/i, /\bteorica\b/i],
    },
    libras: {
      kind: "video-libras",
      label: "Video com Libras",
      patterns: [/videos?\s+com\s+libras/i, /video\s+com\s+libras/i, /\blibras\b/i],
    },
    audio: {
      kind: "audio",
      label: "Audio",
      patterns: [/audios?\s+para\s+download/i, /\baudio\b/i, /\bpodcast\b/i],
    },
    material: {
      kind: "material-escrito",
      label: "Material Escrito",
      patterns: [/material\s+escrito/i, /material\s+para\s+download/i, /\bpdf\b/i],
    },
  });

  const DIRECT_MP4_PATTERN = /\.mp4(?:$|[?#])/i;
  const AUDIO_PATTERN = /\.(mp3|wav|aac|m4a|ogg)(?:$|[?#])/i;
  const MATERIAL_PATTERN = /\.(pdf|doc|docx|ppt|pptx|xls|xlsx|zip|rar)(?:$|[?#])/i;
  const DOWNLOAD_SECTION_DEFINITIONS = Object.freeze([
    SECTION_DEFINITIONS.videoDownload,
    SECTION_DEFINITIONS.libras,
    SECTION_DEFINITIONS.audio,
  ]);

  function detectPage() {
    const pageSignals = inspectPageSignals();
    const pageType = pageSignals.isCentralMedia ? "central-media" : "unknown";
    const data = pageType === "central-media" ? extractCentralMedia(pageSignals) : null;

    return {
      type: pageType,
      data,
      meta: buildResponseMeta(pageType, pageSignals),
    };
  }

  function inspectPageSignals() {
    const comparableTitle = normalizeComparableText(document.title);
    const comparableUrl = normalizeComparableText(window.location.href);
    const comparableBody = normalizeComparableText(document.body?.innerText || "");
    const rawBlocks = findDownloadBlocks();
    const blockSummaries = rawBlocks.map(inspectDownloadBlockStructure);
    const lessonHeaders = extractLessonHeaders();
    const videoSectionCount = blockSummaries.reduce(
      (count, summary) => count + summary.sections.filter((section) => section.kind === "video-download").length,
      0
    );

    return {
      titleMatches:
        comparableTitle.includes("sala virtual atividade") ||
        comparableTitle.includes("ava univirtus") ||
        comparableTitle.includes("central de midia"),
      bodyHasCentralMedia: comparableBody.includes("central de midia"),
      bodyHasVideoSection: comparableBody.includes("video para download"),
      urlLooksRelated:
        comparableUrl.includes("univirtus") ||
        comparableUrl.includes("central-de-midia") ||
        comparableUrl.includes("central_de_midia"),
      lessonHeadersDetected: lessonHeaders.length,
      downloadBlocksDetected: rawBlocks.length,
      videoSectionBlocksDetected: videoSectionCount,
      selectors: {
        blockSelector: DOWNLOAD_BLOCK_SELECTOR,
        labelSelector: `${DOWNLOAD_BLOCK_SELECTOR} ${DOWNLOAD_SECTION_LABEL_SELECTOR}`,
        listSelector: `${DOWNLOAD_BLOCK_SELECTOR} ${DOWNLOAD_SECTION_LIST_SELECTOR}`,
        anchorSelector: `${DOWNLOAD_BLOCK_SELECTOR} a[href]`,
      },
      isCentralMedia:
        videoSectionCount > 0 ||
        (rawBlocks.length > 0 &&
          (comparableTitle.includes("sala virtual atividade") ||
            comparableTitle.includes("central de midia") ||
            comparableBody.includes("central de midia"))),
    };
  }

  function buildResponseMeta(pageType, pageSignals) {
    return {
      pageType,
      readyState: document.readyState,
      url: window.location.href,
      title: document.title || "",
      extractedAt: new Date().toISOString(),
      pageSignals,
    };
  }

  function extractCentralMedia(pageSignals) {
    const disciplineName = extractDisciplineName();
    const orderedElements = getOrderedElements();
    const elementOrder = new Map(orderedElements.map((element, index) => [element, index]));
    const lessonHeaders = extractLessonHeaders(elementOrder);
    const rawBlocks = findDownloadBlocks();
    const blockResults = rawBlocks.map((block) => parseDownloadBlock(block, elementOrder));
    const lessons = buildLessonsFromBlocks(lessonHeaders, blockResults, elementOrder);
    const diagnostics = buildDiagnostics(pageSignals, lessonHeaders, blockResults, lessons);

    logDebug("central-media-signals", pageSignals);
    logDebug("central-media-lesson-headers", lessonHeaders.map((header) => ({
      order: header.order,
      text: header.rawHeader,
      domOrder: header.domOrder,
    })));
    logDebug("central-media-blocks", blockResults.map(summarizeBlockForLog));
    logDebug("central-media-lessons", lessons.map(summarizeLessonForLog));
    logDebug("central-media-selectors", diagnostics.selectors);

    return {
      disciplineName,
      url: window.location.href,
      lessons,
      summary: {
        lessonCount: lessons.length,
        videoCount: diagnostics.validVideoLinksFound,
        sectionCount: diagnostics.videoSectionBlocksDetected,
        downloadSectionCount: diagnostics.videoSectionBlocksDetected,
      },
      diagnostics,
      capturedAt: new Date().toISOString(),
    };
  }

  function extractDisciplineName() {
    const selectors = [
      '[class*="breadcrumb"] li:last-child',
      '[class*="disciplina"] h1',
      '[class*="disciplina"] h2',
      '[class*="titulo-disciplina"]',
      '[class*="disciplina-nome"]',
      '[class*="page-title"]',
      "h1",
      "h2",
    ];

    for (const selector of selectors) {
      const elements = Array.from(document.querySelectorAll(selector));

      for (const element of elements) {
        const text = cleanupDisciplineName(getMeaningfulElementText(element));

        if (!text || looksLikeCentralMediaLabel(text) || Boolean(parseStandaloneLessonHeading(text))) {
          continue;
        }

        return text;
      }
    }

    return (
      cleanupDisciplineName(document.title).replace(/Sala Virtual Atividade\s*-\s*AVA UNIVIRTUS/i, "") ||
      "Disciplina"
    );
  }

  function findDownloadBlocks() {
    return Array.from(document.querySelectorAll(DOWNLOAD_BLOCK_SELECTOR));
  }

  function getOrderedElements() {
    return Array.from(document.body?.querySelectorAll("*") || []);
  }

  function extractLessonHeaders(elementOrder = null) {
    const entries = Array.from(document.querySelectorAll(LESSON_HEADER_SELECTOR))
      .map((element) => {
        const rawHeader = normalizeText(getMeaningfulElementText(element));
        const parsed = parseStandaloneLessonHeading(rawHeader);

        if (!parsed) {
          return null;
        }

        return {
          element,
          rawHeader,
          order: parsed.order,
          title: parsed.title,
          label: parsed.label,
          displayTitle: parsed.displayTitle,
          domOrder: elementOrder?.get(element) ?? -1,
        };
      })
      .filter(Boolean)
      .filter((entry) => !hasMatchingAncestor(entry.element, parseStandaloneLessonHeading))
      .sort((left, right) => compareDomOrder(left.element, right.element));

    return dedupeLessonHeaders(entries, elementOrder);
  }

  function dedupeLessonHeaders(entries, elementOrder) {
    const unique = [];

    entries.forEach((entry) => {
      const previous = unique[unique.length - 1];

      if (!previous) {
        unique.push(entry);
        return;
      }

      const sameOrder = previous.order === entry.order;
      const previousContains = previous.element.contains(entry.element);
      const currentContains = entry.element.contains(previous.element);
      const closeEnough =
        sameOrder &&
        elementOrder &&
        previous.domOrder >= 0 &&
        entry.domOrder >= 0 &&
        Math.abs(entry.domOrder - previous.domOrder) <= 6;

      if (sameOrder && previousContains) {
        unique[unique.length - 1] = entry;
        return;
      }

      if (sameOrder && currentContains) {
        return;
      }

      if (closeEnough) {
        unique[unique.length - 1] = entry;
        return;
      }

      unique.push(entry);
    });

    return unique;
  }

  function parseStandaloneLessonHeading(text) {
    const normalized = normalizeText(text);

    if (!normalized || normalized.length > LESSON_HEADER_MAX_LENGTH) {
      return null;
    }

    const repeatedHeadings = normalized.match(/AULA\s+\d+\b/gi) || [];

    if (repeatedHeadings.length !== 1) {
      return null;
    }

    if (
      /video\s+para\s+download|videos?\s+com\s+libras|\baudio\b|material\s+escrito|aula\s+teorica|aula\s+pratica/i.test(
        normalized
      )
    ) {
      return null;
    }

    const match = normalized.match(/^AULA\s+(\d+)(?:\s*[-:|]\s*(.*))?$/i);

    if (!match) {
      return null;
    }

    const order = parseInt(match[1], 10);

    if (!Number.isFinite(order) || order <= 0) {
      return null;
    }

    const title = normalizeText(match[2] || "");
    const label = `Aula ${String(order).padStart(2, "0")}`;

    return {
      order,
      title,
      label,
      displayTitle: title ? `${label} - ${title}` : label,
    };
  }

  function inspectDownloadBlockStructure(block) {
    const markerTexts = collectBlockMarkerTexts(block);
    const combinedText = normalizeComparableText(markerTexts.join(" | ") || block.textContent);
    const sections = collectDownloadSections(block);

    return {
      sections,
      sectionKinds: {
        videoDownload:
          sections.some((section) => section.kind === SECTION_DEFINITIONS.videoDownload.kind) ||
          matchesAnyPattern(SECTION_DEFINITIONS.videoDownload.patterns, combinedText),
        pratica: matchesAnyPattern(SECTION_DEFINITIONS.pratica.patterns, combinedText),
        teorica: matchesAnyPattern(SECTION_DEFINITIONS.teorica.patterns, combinedText),
        libras:
          sections.some((section) => section.kind === SECTION_DEFINITIONS.libras.kind) ||
          matchesAnyPattern(SECTION_DEFINITIONS.libras.patterns, combinedText),
        audio:
          sections.some((section) => section.kind === SECTION_DEFINITIONS.audio.kind) ||
          matchesAnyPattern(SECTION_DEFINITIONS.audio.patterns, combinedText),
        material:
          sections.some((section) => section.kind === SECTION_DEFINITIONS.material.kind) ||
          matchesAnyPattern(SECTION_DEFINITIONS.material.patterns, combinedText),
      },
      markerTexts,
    };
  }

  function collectBlockMarkerTexts(block) {
    const labelledElements = Array.from(block.querySelectorAll(BLOCK_LABEL_SELECTOR))
      .map((element) => normalizeText(getMeaningfulElementText(element)))
      .filter((text) => text && text.length <= 120);

    return uniqueValues(labelledElements);
  }

  function collectDownloadSections(block) {
    return Array.from(block.querySelectorAll(DOWNLOAD_SECTION_LABEL_SELECTOR))
      .map((labelElement) => {
        const labelText = normalizeText(getMeaningfulElementText(labelElement));
        const definition = matchDownloadSectionDefinition(labelText);

        if (!definition) {
          return null;
        }

        return {
          kind: definition.kind,
          label: definition.label,
          labelText,
          labelElement,
          listElement: findAssociatedListElement(labelElement, block),
        };
      })
      .filter(Boolean);
  }

  function matchDownloadSectionDefinition(text) {
    const comparable = normalizeComparableText(text);

    return (
      DOWNLOAD_SECTION_DEFINITIONS.find((definition) =>
        definition.patterns.some((pattern) => pattern.test(comparable))
      ) || null
    );
  }

  function findAssociatedListElement(labelElement, block) {
    let current = labelElement.nextElementSibling;

    while (current) {
      if (current.tagName === "UL") {
        return current;
      }

      if (matchesDownloadSectionLabelElement(current)) {
        break;
      }

      const nestedList = current.querySelector?.("ul");

      if (nestedList) {
        return nestedList;
      }

      current = current.nextElementSibling;
    }

    const followingLists = Array.from(block.querySelectorAll(DOWNLOAD_SECTION_LIST_SELECTOR)).filter((list) =>
      Boolean(labelElement.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING)
    );

    return (
      followingLists.find((list) => !hasIntermediateDownloadSectionLabel(labelElement, list, block)) || null
    );
  }

  function matchesDownloadSectionLabelElement(element) {
    return element.tagName === "P" && Boolean(matchDownloadSectionDefinition(getMeaningfulElementText(element)));
  }

  function hasIntermediateDownloadSectionLabel(labelElement, listElement, block) {
    return Array.from(block.querySelectorAll(DOWNLOAD_SECTION_LABEL_SELECTOR)).some((candidate) => {
      if (candidate === labelElement) {
        return false;
      }

      const isBetween =
        Boolean(labelElement.compareDocumentPosition(candidate) & Node.DOCUMENT_POSITION_FOLLOWING) &&
        Boolean(candidate.compareDocumentPosition(listElement) & Node.DOCUMENT_POSITION_FOLLOWING);

      return isBetween && Boolean(matchDownloadSectionDefinition(getMeaningfulElementText(candidate)));
    });
  }

  function parseDownloadBlock(block, elementOrder) {
    const inspected = inspectDownloadBlockStructure(block);
    const sections = inspected.sections.map((section) => ({
      ...section,
      anchors: section.listElement ? extractAnchorsFromList(section.listElement, elementOrder) : [],
    }));
    const observedLinks = sections.flatMap((section) =>
      section.anchors
        .map((anchorRef) => buildObservedLink(anchorRef.element, block, section, inspected.sectionKinds))
        .filter(Boolean)
    );
    const validVideoLinks = [];
    const ignoredLinks = [];
    const ignoredByType = {
      "video-libras": 0,
      audio: 0,
      "non-mp4": 0,
    };

    observedLinks.forEach((link) => {
      if (link.mediaType === "video_principal") {
        validVideoLinks.push(link);
        return;
      }

      ignoredLinks.push(link);
      ignoredByType[link.diagnosticBucket] = (ignoredByType[link.diagnosticBucket] || 0) + 1;
    });

    return {
      element: block,
      domOrder: elementOrder.get(block) ?? Number.MAX_SAFE_INTEGER,
      markerTexts: inspected.markerTexts,
      sections,
      sectionKinds: inspected.sectionKinds,
      anchorsFound: sections.reduce((count, section) => count + section.anchors.length, 0),
      videoDownloadSectionsFound: sections.filter(
        (section) => section.kind === SECTION_DEFINITIONS.videoDownload.kind
      ).length,
      videoDownloadAnchorsFound: sections
        .filter((section) => section.kind === SECTION_DEFINITIONS.videoDownload.kind)
        .reduce((count, section) => count + section.anchors.length, 0),
      observedLinks,
      validVideoLinks,
      ignoredLinks,
      ignoredByType,
      blockLabel: pickPrimaryBlockLabel(inspected.sectionKinds),
    };
  }

  function pickPrimaryBlockLabel(sectionKinds) {
    if (sectionKinds.videoDownload) {
      return SECTION_DEFINITIONS.videoDownload.label;
    }

    if (sectionKinds.libras) {
      return SECTION_DEFINITIONS.libras.label;
    }

    if (sectionKinds.audio) {
      return SECTION_DEFINITIONS.audio.label;
    }

    if (sectionKinds.material) {
      return SECTION_DEFINITIONS.material.label;
    }

    return "Bloco de download";
  }

  function extractAnchorsFromList(listElement, elementOrder) {
    const anchors = Array.from(listElement.querySelectorAll("a[href]")).map((element) => ({
      element,
      order: elementOrder.get(element) ?? Number.MAX_SAFE_INTEGER,
    }));

    return dedupeAnchorRefs(anchors);
  }

  function dedupeAnchorRefs(anchors) {
    const seen = new Set();

    return anchors.filter((anchor) => {
      const url = readDirectAnchorHref(anchor.element);
      const label = normalizeText(anchor.element.textContent);
      const key = `${url}::${label}`;

      if (!url || seen.has(key)) {
        return false;
      }

      seen.add(key);
      return true;
    });
  }

  function buildObservedLink(anchor, block, section, blockKinds) {
    const href = readDirectAnchorHref(anchor);

    if (!href) {
      return null;
    }

    const label = extractVisibleAnchorText(anchor);
    const contextText = extractAnchorContext(anchor, block, section);
    const classification = classifyObservedLink(href, label, contextText, section);
    const role = classifyPrimaryVideoRole(label, contextText, blockKinds);

    return {
      label: label || contextText || "Video",
      url: href,
      contextText,
      mediaType: classification.mediaType,
      diagnosticReason: classification.diagnosticReason,
      diagnosticBucket: classification.diagnosticBucket,
      sectionKind: role,
      sectionLabel: section.label,
      sourceSectionKind: section.kind,
      directHref: href,
      selectorSource: `${DOWNLOAD_BLOCK_SELECTOR} ${DOWNLOAD_SECTION_LABEL_SELECTOR} + ${DOWNLOAD_SECTION_LIST_SELECTOR} a[href]`,
    };
  }

  function classifyObservedLink(url, label, contextText, section) {
    if (section.kind === SECTION_DEFINITIONS.libras.kind) {
      return {
        mediaType: "video_libras",
        diagnosticBucket: "video-libras",
        diagnosticReason: "ignorado por ser libras",
      };
    }

    if (section.kind === SECTION_DEFINITIONS.audio.kind || AUDIO_PATTERN.test(url)) {
      return {
        mediaType: "audio",
        diagnosticBucket: "audio",
        diagnosticReason: "ignorado por ser audio",
      };
    }

    if (section.kind === SECTION_DEFINITIONS.videoDownload.kind && DIRECT_MP4_PATTERN.test(url)) {
      return {
        mediaType: "video_principal",
        diagnosticBucket: "video_principal",
        diagnosticReason: "aceito como mp4",
      };
    }

    return {
      mediaType: "rejected",
      diagnosticBucket: "non-mp4",
      diagnosticReason: "ignorado por nao ter .mp4",
    };
  }

  function classifyPrimaryVideoRole(label, contextText, sectionKinds) {
    const comparable = normalizeComparableText(`${label} ${contextText}`);

    if (sectionKinds.pratica || comparable.includes("pratica")) {
      return "pratica";
    }

    return "teorica";
  }

  function readDirectAnchorHref(anchor) {
    const rawHref = anchor.getAttribute("href") || anchor.href;

    if (!rawHref) {
      return null;
    }

    const trimmed = String(rawHref).trim();

    if (!trimmed || trimmed === "#" || /^javascript:/i.test(trimmed)) {
      return null;
    }

    try {
      const resolved = new URL(trimmed, window.location.href).href;
      return /^https?:\/\//i.test(resolved) ? resolved : null;
    } catch {
      return null;
    }
  }

  function extractVisibleAnchorText(anchor) {
    const candidates = [
      normalizeText(anchor.textContent),
      normalizeText(anchor.getAttribute("title")),
      normalizeText(anchor.getAttribute("aria-label")),
    ];

    return candidates.find((candidate) => candidate) || "";
  }

  function extractAnchorContext(anchor, block, section) {
    const containers = [
      anchor.closest("li"),
      anchor.closest("tr"),
      anchor.closest("td"),
      anchor.closest("p"),
      anchor.parentElement,
      section?.listElement,
      section?.labelElement,
      block,
    ];

    return uniqueValues(
      containers.map((element) => normalizeText(element?.textContent || "")).filter(Boolean)
    )
      .slice(0, 3)
      .join(" | ");
  }

  function buildLessonsFromBlocks(lessonHeaders, blockResults, elementOrder) {
    const lessonsByKey = new Map();

    lessonHeaders.forEach((header) => {
      lessonsByKey.set(header.label, createLessonRecord(header));
    });

    blockResults.forEach((blockResult, index) => {
      const lessonHeader = resolveLessonHeaderForBlock(blockResult, lessonHeaders, elementOrder);
      const lessonKey = lessonHeader?.label || `Aula ${String(index + 1).padStart(2, "0")}`;
      const lesson =
        lessonsByKey.get(lessonKey) ||
        createLessonRecord({
          order: index + 1,
          label: lessonKey,
          title: "",
          displayTitle: lessonKey,
          rawHeader: lessonKey,
        });

      mergeBlockIntoLesson(lesson, blockResult);
      lessonsByKey.set(lessonKey, lesson);
    });

    return Array.from(lessonsByKey.values())
      .map(finalizeLessonRecord)
      .sort((left, right) => left.order - right.order);
  }

  function createLessonRecord(header) {
    return {
      order: header.order,
      label: header.label,
      title: header.title || "",
      displayTitle: header.displayTitle || header.label,
      rawHeader: header.rawHeader || header.label,
      blockCount: 0,
      videoSectionBlockCount: 0,
      anchorsProcessed: 0,
      contextTexts: [header.rawHeader || header.displayTitle || header.label],
      sectionLabels: new Set(),
      downloadLinks: [],
      ignoredLinksFound: 0,
      ignoredByType: {
        "video-libras": 0,
        audio: 0,
        "non-mp4": 0,
      },
    };
  }

  function resolveLessonHeaderForBlock(blockResult, lessonHeaders, elementOrder) {
    if (lessonHeaders.length === 0) {
      return null;
    }

    let selected = null;

    lessonHeaders.forEach((header, index) => {
      const currentOrder = header.domOrder >= 0 ? header.domOrder : elementOrder.get(header.element);
      const nextOrder =
        index + 1 < lessonHeaders.length
          ? lessonHeaders[index + 1].domOrder >= 0
            ? lessonHeaders[index + 1].domOrder
            : elementOrder.get(lessonHeaders[index + 1].element)
          : Number.MAX_SAFE_INTEGER;

      if (
        currentOrder !== undefined &&
        currentOrder !== null &&
        blockResult.domOrder >= currentOrder &&
        blockResult.domOrder < nextOrder
      ) {
        selected = header;
      }
    });

    if (selected) {
      return selected;
    }

    return findNearestPreviousLessonHeader(blockResult.domOrder, lessonHeaders);
  }

  function findNearestPreviousLessonHeader(blockOrder, lessonHeaders) {
    let selected = null;

    lessonHeaders.forEach((header) => {
      if (header.domOrder >= 0 && header.domOrder <= blockOrder) {
        selected = header;
      }
    });

    return selected || lessonHeaders[0] || null;
  }

  function mergeBlockIntoLesson(lesson, blockResult) {
    lesson.blockCount += 1;
    lesson.anchorsProcessed += blockResult.anchorsFound;
    lesson.contextTexts.push(...blockResult.markerTexts);

    if (blockResult.sectionKinds.videoDownload) {
      lesson.videoSectionBlockCount += 1;
      lesson.sectionLabels.add(SECTION_DEFINITIONS.videoDownload.label);
    }

    if (blockResult.sectionKinds.teorica) {
      lesson.sectionLabels.add(SECTION_DEFINITIONS.teorica.label);
    }

    if (blockResult.sectionKinds.pratica) {
      lesson.sectionLabels.add(SECTION_DEFINITIONS.pratica.label);
    }

    if (blockResult.sectionKinds.material) {
      lesson.sectionLabels.add(SECTION_DEFINITIONS.material.label);
    }

    blockResult.validVideoLinks.forEach((link) => lesson.downloadLinks.push(link));
    lesson.ignoredLinksFound += blockResult.ignoredLinks.length;

    Object.entries(blockResult.ignoredByType).forEach(([type, count]) => {
      lesson.ignoredByType[type] = (lesson.ignoredByType[type] || 0) + count;
    });
  }

  function finalizeLessonRecord(record) {
    const downloadLinks = dedupeDownloadLinks(record.downloadLinks);
    const sections = buildSectionSummaries(record.sectionLabels, downloadLinks.length);

    return {
      order: record.order,
      label: record.label,
      title: record.title,
      displayTitle: record.displayTitle,
      rawHeader: record.rawHeader,
      sections,
      downloadLinks,
      diagnostics: {
        downloadSectionDetected: record.videoSectionBlockCount > 0,
        sectionMarkersFound: sections.filter((section) => section.found).length,
        blockCount: record.blockCount,
        videoSectionBlockCount: record.videoSectionBlockCount,
        anchorsProcessed: record.anchorsProcessed,
        validVideoLinksFound: downloadLinks.length,
        ignoredLinksFound: record.ignoredLinksFound,
        ignoredByType: record.ignoredByType,
      },
    };
  }

  function dedupeDownloadLinks(links) {
    const seen = new Set();

    return links.filter((link) => {
      const key = `${link.url}::${normalizeText(link.label)}`;

      if (seen.has(key)) {
        return false;
      }

      seen.add(key);
      return true;
    });
  }

  function buildSectionSummaries(sectionLabels, videoCount) {
    const labels = sectionLabels instanceof Set ? sectionLabels : new Set(sectionLabels || []);

    return [
      SECTION_DEFINITIONS.teorica,
      SECTION_DEFINITIONS.pratica,
      SECTION_DEFINITIONS.material,
      SECTION_DEFINITIONS.videoDownload,
    ].map((definition) => ({
      kind: definition.kind,
      label: definition.label,
      found: labels.has(definition.label),
      matchedLabels: labels.has(definition.label) ? [definition.label] : [],
      linkCount: definition.kind === SECTION_DEFINITIONS.videoDownload.kind ? videoCount : 0,
    }));
  }

  function buildDiagnostics(pageSignals, lessonHeaders, blockResults, lessons) {
    const diagnostics = lessons.reduce(
      (state, lesson) => {
        state.sectionMarkersFound += lesson.diagnostics.sectionMarkersFound;
        state.lessonsWithProcessedBlocks += lesson.diagnostics.blockCount > 0 ? 1 : 0;
        state.downloadSectionsDetected += lesson.diagnostics.videoSectionBlockCount;
        state.anchorsProcessed += lesson.diagnostics.anchorsProcessed;
        state.validVideoLinksFound += lesson.diagnostics.validVideoLinksFound;
        state.ignoredLinksFound += lesson.diagnostics.ignoredLinksFound;

        Object.entries(lesson.diagnostics.ignoredByType).forEach(([key, value]) => {
          state.ignoredByType[key] = (state.ignoredByType[key] || 0) + value;
        });

        return state;
      },
      {
        pageRecognized: pageSignals.isCentralMedia,
        lessonHeadersDetected: lessonHeaders.length,
        lessonBlocksDetected: lessons.length,
        lessonsWithProcessedBlocks: 0,
        sectionMarkersFound: 0,
        downloadBlocksProcessed: blockResults.length,
        videoSectionBlocksDetected: blockResults.reduce(
          (count, block) => count + block.videoDownloadSectionsFound,
          0
        ),
        downloadSectionsDetected: 0,
        videoDownloadAnchorsProcessed: blockResults.reduce(
          (count, block) => count + block.videoDownloadAnchorsFound,
          0
        ),
        anchorsProcessed: 0,
        validVideoLinksFound: 0,
        ignoredLinksFound: 0,
        ignoredByType: {
          "video-libras": 0,
          audio: 0,
          "non-mp4": 0,
        },
      }
    );

    diagnostics.groupingMismatch =
      diagnostics.lessonHeadersDetected > 1 &&
      diagnostics.downloadBlocksProcessed > 1 &&
      diagnostics.lessonsWithProcessedBlocks <= 1;

    const status = resolveDiagnosticsStatus(diagnostics);

    return {
      ...diagnostics,
      status,
      selectors: {
        blockSelector: DOWNLOAD_BLOCK_SELECTOR,
        labelSelector: `${DOWNLOAD_BLOCK_SELECTOR} ${DOWNLOAD_SECTION_LABEL_SELECTOR}`,
        listSelector: `${DOWNLOAD_BLOCK_SELECTOR} ${DOWNLOAD_SECTION_LIST_SELECTOR}`,
        anchorSelector: `${DOWNLOAD_BLOCK_SELECTOR} ${DOWNLOAD_SECTION_LABEL_SELECTOR} + ${DOWNLOAD_SECTION_LIST_SELECTOR} a[href]`,
        matchedDownloadBlocks: diagnostics.downloadBlocksProcessed,
        matchedVideoSectionBlocks: diagnostics.videoSectionBlocksDetected,
        matchedAnchors: diagnostics.videoDownloadAnchorsProcessed,
      },
      notes: buildDiagnosticNotes(status, diagnostics),
    };
  }

  function resolveDiagnosticsStatus(diagnostics) {
    if (diagnostics.groupingMismatch) {
      return "lesson-grouping-error";
    }

    if (diagnostics.videoSectionBlocksDetected === 0) {
      return "download-section-missing";
    }

    if (diagnostics.videoDownloadAnchorsProcessed === 0) {
      return "download-section-without-links";
    }

    if (diagnostics.validVideoLinksFound === 0) {
      return "download-section-without-valid-videos";
    }

    return "videos-found";
  }

  function buildDiagnosticNotes(status, diagnostics) {
    const notes = [
      `${diagnostics.lessonHeadersDetected} heading(s) de aula detectado(s).`,
      `${diagnostics.downloadBlocksProcessed} bloco(s) ${DOWNLOAD_BLOCK_SELECTOR} processado(s).`,
      `${diagnostics.videoSectionBlocksDetected} secao(oes) 'Video para download' encontrada(s).`,
      `${diagnostics.videoDownloadAnchorsProcessed} anchor(s) lido(s) nas listas <ul> de video.`,
      `${diagnostics.validVideoLinksFound} href(s) diretos .mp4 aceito(s) como midia principal, mesmo quando o navegador abre o arquivo.`,
      `Ignorados: libras ${diagnostics.ignoredByType["video-libras"] || 0}, audio ${
        diagnostics.ignoredByType.audio || 0
      }, sem .mp4 ${diagnostics.ignoredByType["non-mp4"] || 0}.`,
    ];

    if (status === "lesson-grouping-error") {
      notes.push(
        "Os blocos foram reconhecidos, mas a consolidacao por aula ficou inconsistente. O popup deve bloquear o fluxo ate a correção."
      );
    }

    if (status === "download-section-without-valid-videos") {
      notes.push(
        "A estrutura foi reconhecida, mas nenhum href direto .mp4 util foi confirmado como video principal."
      );
      notes.push(
        "Se os links existem visualmente, verifique se o filtro de extensao esta rejeitando a lista associada ao <p> 'Video para download'."
      );
    }

    return notes;
  }

  function summarizeBlockForLog(block) {
    return {
      order: block.domOrder,
      label: block.blockLabel,
      sections: block.sections.map((section) => ({
        kind: section.kind,
        label: section.label,
        anchors: section.anchors.length,
        hasList: Boolean(section.listElement),
      })),
      anchors: block.anchorsFound,
      sectionKinds: block.sectionKinds,
      videos: block.validVideoLinks.length,
      ignored: block.ignoredByType,
      anchorDiagnostics: block.observedLinks.map((link) => ({
        label: link.label,
        url: link.url,
        sourceSectionKind: link.sourceSectionKind,
        mediaType: link.mediaType,
        diagnosticReason: link.diagnosticReason,
      })),
    };
  }

  function summarizeLessonForLog(lesson) {
    return {
      order: lesson.order,
      label: lesson.label,
      blockCount: lesson.diagnostics.blockCount,
      anchorsProcessed: lesson.diagnostics.anchorsProcessed,
      videos: lesson.downloadLinks.length,
      ignored: lesson.diagnostics.ignoredByType,
    };
  }

  function matchesAnyPattern(patterns, comparableText) {
    return patterns.some((pattern) => pattern.test(comparableText));
  }

  function looksLikeCentralMediaLabel(text) {
    const comparable = normalizeComparableText(text);
    return comparable.includes("central de midia") || comparable.includes("sala virtual atividade");
  }

  function cleanupDisciplineName(value) {
    return normalizeText(value)
      .replace(/\|\s*Central de Midia.*$/i, "")
      .replace(/Central de Midia\s*[-|]\s*/i, "")
      .replace(/Sala Virtual Atividade\s*-\s*AVA UNIVIRTUS/i, "")
      .trim();
  }

  function hasMatchingAncestor(element, matcher) {
    let current = element.parentElement;

    while (current && current !== document.body) {
      if (matcher(normalizeText(getMeaningfulElementText(current)))) {
        return true;
      }

      current = current.parentElement;
    }

    return false;
  }

  function getMeaningfulElementText(element) {
    const directText = normalizeText(
      Array.from(element.childNodes || [])
        .filter((node) => node.nodeType === Node.TEXT_NODE)
        .map((node) => node.textContent)
        .join(" ")
    );

    if (directText) {
      return directText;
    }

    const ariaLabel = normalizeText(element.getAttribute("aria-label"));

    if (ariaLabel) {
      return ariaLabel;
    }

    return normalizeText(element.textContent);
  }

  function normalizeText(value) {
    return String(value || "").trim().replace(/\s+/g, " ");
  }

  function normalizeComparableText(value) {
    return normalizeText(value)
      .normalize("NFD")
      .replace(/[\u0300-\u036f]/g, "")
      .toLowerCase();
  }

  function uniqueValues(values) {
    return Array.from(new Set(values.filter(Boolean)));
  }

  function compareDomOrder(left, right) {
    if (left === right) {
      return 0;
    }

    const position = left.compareDocumentPosition(right);

    if (position & Node.DOCUMENT_POSITION_FOLLOWING) {
      return -1;
    }

    return 1;
  }

  function logDebug(stage, payload) {
    try {
      console.debug("[StudyHub Sync]", stage, payload);
    } catch {
      // noop
    }
  }

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.action === "PING") {
      const pageSignals = inspectPageSignals();

      sendResponse({
        ok: true,
        type: "pong",
        meta: buildResponseMeta(pageSignals.isCentralMedia ? "central-media" : "unknown", pageSignals),
      });
      return false;
    }

    if (message?.action === "EXTRACT") {
      try {
        sendResponse({ ok: true, result: detectPage() });
      } catch (error) {
        sendResponse({
          ok: false,
          error: {
            code: "EXTRACTION_FAILED",
            message: error?.message || "Falha interna na extracao do content script.",
          },
        });
      }

      return false;
    }

    return false;
  });
})();
