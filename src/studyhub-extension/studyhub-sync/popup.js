const POPUP_STATES = Object.freeze({
  IDLE: "idle",
  CHECKING_PAGE: "checking-page",
  CONNECTING: "connecting",
  EXTRACTING: "extracting",
  EMPTY: "empty",
  READY: "ready",
  DOWNLOADING: "downloading",
  DOWNLOAD_SUCCESS: "download-success",
  DOWNLOAD_ERROR: "download-error",
  INVALID_PAGE: "invalid-page",
  COMMUNICATION_ERROR: "communication-error",
});

const TIMEOUTS = Object.freeze({
  TAB_QUERY_MS: 3000,
  MESSAGE_MS: 4500,
  SCRIPT_INJECTION_MS: 4500,
  DOWNLOAD_MS: 7000,
});

const DEFAULT_BUTTON_TEXT = Object.freeze({
  scan: "Escanear Central",
  checking: "Validando...",
  connecting: "Conectando...",
  extracting: "Extraindo...",
  download: "Baixar curso por pasta",
  downloading: "Baixando...",
});

const DEFAULT_SUMMARY = Object.freeze({
  page: "Aguardando",
  section: "Aguardando",
  discipline: "--",
  lessons: "0",
  videos: "0",
  downloads: "0/0",
});

const DEFAULT_METRICS = Object.freeze({
  blocks: "0",
  videoSections: "0",
  anchors: "0",
  mp4: "0",
  libras: "0",
  audio: "0",
});

const DOWNLOAD_HINTS = Object.freeze([
  "Os arquivos vao para a pasta padrao de Downloads usando caminhos relativos.",
  "Depois voce pode mover a pasta manualmente e seleciona-la no StudyHub.",
  "O navegador pode renomear arquivos se o mesmo nome ja existir.",
]);

const runtimeState = {
  state: POPUP_STATES.IDLE,
  plan: null,
  isScanning: false,
  isDownloading: false,
  downloadsStarted: 0,
  lastActiveTab: null,
};

const elements = {
  status: document.getElementById("status"),
  statusTitle: document.getElementById("status-title"),
  statusDetail: document.getElementById("status-detail"),
  previewArea: document.getElementById("preview-area"),
  previewTitle: document.getElementById("preview-title"),
  previewList: document.getElementById("preview-list"),
  summaryPage: document.getElementById("summary-page"),
  summarySection: document.getElementById("summary-section"),
  summaryDiscipline: document.getElementById("summary-discipline"),
  summaryLessons: document.getElementById("summary-lessons"),
  summaryVideos: document.getElementById("summary-videos"),
  summaryDownloads: document.getElementById("summary-downloads"),
  metricsArea: document.getElementById("metrics-area"),
  metricsBlocks: document.getElementById("metrics-blocks"),
  metricsVideoSections: document.getElementById("metrics-video-sections"),
  metricsAnchors: document.getElementById("metrics-anchors"),
  metricsMp4: document.getElementById("metrics-mp4"),
  metricsLibras: document.getElementById("metrics-libras"),
  metricsAudio: document.getElementById("metrics-audio"),
  instructions: document.getElementById("instructions"),
  instructionsTitle: document.getElementById("instructions-title"),
  instructionsList: document.getElementById("instructions-list"),
  includeMetadata: document.getElementById("include-metadata"),
  scanButton: document.getElementById("btn-scan"),
  downloadButton: document.getElementById("btn-download"),
};

document.addEventListener("DOMContentLoaded", initializePopup);

function initializePopup() {
  elements.scanButton.addEventListener("click", () => scan({ auto: false }));
  elements.downloadButton.addEventListener("click", downloadCourseFolder);

  renderSummary(DEFAULT_SUMMARY);
  renderMetrics(null);
  renderPreview(null);
  applyState(POPUP_STATES.IDLE, {
    title: "Pronto para ler a Central de Midia",
    detail: "Abra a pagina Central de Midia da disciplina para gerar um curso por pasta.",
    instructionsTitle: "Fluxo desta etapa",
    instructions: [
      "Esta modalidade gera um curso por pasta dentro de Downloads.",
      "A extensao usa apenas a Central de Midia nesta fase.",
      "Depois voce podera mover a pasta manualmente e seleciona-la no StudyHub.",
    ],
  });

  scan({ auto: true });
}

async function scan({ auto = false } = {}) {
  if (runtimeState.isScanning || runtimeState.isDownloading) {
    return;
  }

  runtimeState.isScanning = true;
  runtimeState.plan = null;
  runtimeState.downloadsStarted = 0;
  renderSummary(DEFAULT_SUMMARY);
  renderMetrics(null);
  renderPreview(null);

  try {
    applyState(POPUP_STATES.CHECKING_PAGE, {
      title: "Validando a aba ativa",
      detail: auto
        ? "Conferindo automaticamente se a aba atual e a Central de Midia."
        : "Conferindo se a aba atual esta pronta para leitura.",
      instructionsTitle: "O que esta acontecendo",
      instructions: [
        "A extensao valida o dominio e a pagina antes de extrair os dados.",
        "Se a tela ainda estiver carregando, o popup vai bloquear o fluxo.",
      ],
    });

    const tab = await getActiveTab();
    runtimeState.lastActiveTab = tab;

    const validationError = validateActiveTab(tab);
    if (validationError) {
      throw validationError;
    }

    const connection = await ensureContentScriptReady(tab);

    applyState(POPUP_STATES.EXTRACTING, {
      title: "Extraindo a Central de Midia",
      detail: connection.reinjected
        ? "O content script foi reinjetado e a leitura vai comecar agora."
        : "Lendo disciplina, aulas, secoes e videos disponiveis.",
      instructionsTitle: "O que esta acontecendo",
      instructions: [
        "Apenas a Central de Midia e valida nesta etapa.",
        "A extensao prepara um plano de pasta antes de iniciar downloads.",
      ],
    });

    const result = await requestExtraction(tab.id);
    renderAnalysis(analyzeCaptureResult(result, tab, connection));
  } catch (error) {
    console.error("[StudyHub Sync] scan failed", error);
    handlePopupError(error);
  } finally {
    runtimeState.isScanning = false;
    updateActionButtons();
  }
}

async function downloadCourseFolder() {
  if (runtimeState.isScanning || runtimeState.isDownloading) {
    return;
  }

  if (!runtimeState.plan) {
    handlePopupError(
      createPopupError("download-no-plan", {
        state: POPUP_STATES.DOWNLOAD_ERROR,
        title: "Nada pronto para baixar",
        detail: "Faca primeiro um scan valido da Central de Midia.",
        instructionsTitle: "Como corrigir",
        instructions: [
          "Abra uma Central de Midia valida no Univirtus.",
          "Confira a disciplina, as aulas e os videos detectados.",
          "So depois use o botao de download.",
        ],
      })
    );
    return;
  }

  runtimeState.isDownloading = true;
  runtimeState.downloadsStarted = 0;
  renderSummary(buildSummaryFromPlan(runtimeState.plan, 0));

  const includeMetadata = elements.includeMetadata.checked;

  try {
    const queue = buildDownloadQueue(runtimeState.plan, includeMetadata);

    applyState(POPUP_STATES.DOWNLOADING, {
      title: "Iniciando downloads",
      detail: buildDownloadDetail(runtimeState.plan, 0, includeMetadata),
      instructionsTitle: "O que acontece agora",
      instructions: DOWNLOAD_HINTS,
    });

    const failures = [];

    for (const item of queue) {
      try {
        await downloadQueueItem(item);

        if (item.kind === "video") {
          runtimeState.downloadsStarted += 1;
          renderSummary(buildSummaryFromPlan(runtimeState.plan, runtimeState.downloadsStarted));
        }

        applyState(POPUP_STATES.DOWNLOADING, {
          title: "Iniciando downloads",
          detail: buildDownloadDetail(
            runtimeState.plan,
            runtimeState.downloadsStarted,
            includeMetadata
          ),
          instructionsTitle: "O que acontece agora",
          instructions: DOWNLOAD_HINTS,
        });
      } catch (error) {
        failures.push(error);
      }
    }

    if (failures.length > 0) {
      throw createPopupError("download-partial", {
        state: POPUP_STATES.DOWNLOAD_ERROR,
        title: "Download iniciado parcialmente",
        detail:
          `${runtimeState.downloadsStarted} de ${runtimeState.plan.videoCount} video(s) foram ` +
          `iniciados. Primeiro erro: ${failures[0]?.message || "falha ao iniciar download"}.`,
        instructionsTitle: "Como interpretar",
        instructions: [
          "Parte dos downloads pode depender da sessao ativa ou da politica do navegador.",
          "Confira a fila de Downloads e tente novamente se algo importante falhar.",
          "Se necessario, recarregue a Central de Midia antes de repetir o processo.",
        ],
      });
    }

    applyState(POPUP_STATES.DOWNLOAD_SUCCESS, {
      title: "Curso por pasta iniciado",
      detail:
        `${runtimeState.plan.videoCount} video(s) enviados para Downloads/` +
        `${runtimeState.plan.courseFolderName}.`,
      instructionsTitle: "Proximo passo",
      instructions: [
        "Aguarde o navegador concluir os downloads dos arquivos grandes.",
        "Depois voce pode mover a pasta manualmente para outro local.",
        "No StudyHub, use o fluxo existente de curso por pasta para selecionar a pasta final.",
      ],
    });
  } catch (error) {
    console.error("[StudyHub Sync] download failed", error);
    handlePopupError(error);
  } finally {
    runtimeState.isDownloading = false;
    updateActionButtons();
  }
}

async function ensureContentScriptReady(tab) {
  applyState(POPUP_STATES.CONNECTING, {
    title: "Conectando com a pagina",
    detail: "Tentando falar com o content script da Central de Midia.",
    instructionsTitle: "O que esta acontecendo",
    instructions: [
      "Se a aba nao responder, a extensao tenta reinjetar o script.",
      "Esse passo nao altera a pagina nem faz downloads.",
    ],
  });

  try {
    await pingContentScript(tab.id);
    return { reinjected: false };
  } catch (firstError) {
    if (isTimeoutError(firstError)) {
      throw createPopupError("timeout", {
        state: POPUP_STATES.COMMUNICATION_ERROR,
        title: "Tempo esgotado na comunicacao",
        detail: "A pagina demorou demais para responder ao popup.",
        instructionsTitle: "Como corrigir",
        instructions: [
          "Aguarde a Central de Midia terminar de carregar e tente novamente.",
          "Se o problema continuar, recarregue a aba antes de abrir o popup outra vez.",
        ],
      });
    }

    await injectContentScript(tab.id);
    await pingContentScript(tab.id);
    return { reinjected: true };
  }
}

async function pingContentScript(tabId) {
  const response = await sendTabMessage(tabId, { action: "PING" }, TIMEOUTS.MESSAGE_MS);

  if (!isPlainObject(response) || response.ok !== true || response.type !== "pong") {
    throw createPopupError("invalid-response", {
      state: POPUP_STATES.COMMUNICATION_ERROR,
      title: "Resposta invalida da pagina",
      detail: "A aba respondeu em um formato inesperado.",
      instructionsTitle: "Como corrigir",
      instructions: [
        "Tente escanear novamente.",
        "Se a falha continuar, recarregue a pagina antes da proxima tentativa.",
      ],
    });
  }

  return response;
}

async function requestExtraction(tabId) {
  const response = await sendTabMessage(tabId, { action: "EXTRACT" }, TIMEOUTS.MESSAGE_MS);

  if (!isPlainObject(response) || response.ok !== true || !isValidExtractResult(response.result)) {
    throw createPopupError("invalid-extract", {
      state: POPUP_STATES.COMMUNICATION_ERROR,
      title: "Falha na extracao",
      detail:
        response?.error?.message ||
        "A pagina respondeu sem um resultado valido de extracao da Central de Midia.",
      instructionsTitle: "Como corrigir",
      instructions: [
        "Tente escanear novamente.",
        "Se a falha persistir, recarregue a Central de Midia antes de repetir o processo.",
      ],
    });
  }

  return response.result;
}

async function injectContentScript(tabId) {
  await withTimeout(
    chrome.scripting.executeScript({
      target: { tabId },
      files: ["content.js"],
    }),
    TIMEOUTS.SCRIPT_INJECTION_MS,
    createPopupError("timeout", {
      state: POPUP_STATES.COMMUNICATION_ERROR,
      title: "Tempo esgotado na reinjecao",
      detail: "A reinjecao do content script demorou demais para terminar.",
      instructionsTitle: "Como corrigir",
      instructions: [
        "Recarregue a pagina do Univirtus e tente novamente.",
        "Evite abrir o popup enquanto a pagina ainda estiver navegando internamente.",
      ],
    })
  );
}

function analyzeCaptureResult(result, tab, connection) {
  const loading = isPageStillLoading(tab, result.meta);

  if (result.type !== "central-media") {
    return {
      kind: "invalid-page",
      presentation: {
        title: loading ? "Pagina ainda carregando" : "Pagina nao reconhecida",
        detail: loading
          ? "A aba ainda nao terminou de carregar os elementos da Central de Midia."
          : "A extensao nao encontrou uma Central de Midia valida nesta aba.",
        instructionsTitle: "Como corrigir",
        instructions: loading
          ? [
              "Aguarde o carregamento terminar e tente novamente.",
              "Se a pagina acabou de abrir, espere alguns segundos antes de reescanear.",
            ]
          : [
              "Abra a pagina Central de Midia da disciplina.",
              "Este fluxo nao usa Home, Roteiro de Estudo ou Avaliacoes nesta etapa.",
            ],
      },
      summary: { ...DEFAULT_SUMMARY, page: loading ? "Carregando" : "Invalida" },
      metrics: null,
    };
  }

  const courseData = result.data || {};
  const diagnostics = courseData.diagnostics || buildFallbackDiagnostics(courseData);
  const diagnosticsSummary = buildDiagnosticsSummary(diagnostics);
  const plan = buildCoursePlan(courseData, tab, diagnosticsSummary);
  const metrics = buildMetricsFromDiagnostics(diagnostics);

  if (
    !plan.courseName ||
    plan.lessonCount === 0 ||
    diagnostics.status === "structure-unrecognized" ||
    diagnostics.status === "lesson-grouping-error"
  ) {
    return {
      kind: "empty",
      presentation: {
        title:
          diagnostics.status === "lesson-grouping-error"
            ? "Agrupamento de aulas inconsistente"
            : "Estrutura da pagina nao reconhecida",
        detail: loading
          ? "A pagina parece valida, mas ainda esta carregando os blocos necessarios."
          : diagnostics.status === "lesson-grouping-error"
            ? diagnosticsSummary.detail
            : "A Central de Midia nao trouxe blocos de aula suficientes para montar o parser.",
        instructionsTitle: "Como interpretar",
        instructions: loading
          ? [
              "Espere a pagina terminar de montar o conteudo e escaneie novamente.",
              "Se houver acordeons, deixe as aulas visiveis antes de repetir a captura.",
            ]
          : buildDiagnosticsInstructions(diagnostics, diagnosticsSummary, loading),
      },
      preview: {
        title: plan.courseName || "Central de Midia",
        items:
          diagnostics.status === "lesson-grouping-error"
            ? plan.lessons.map((lesson) => ({
                badge: lesson.label.toUpperCase(),
                badgeClass: "badge-gray",
                name: lesson.displayTitle,
                sub: buildLessonPreviewSubtext(lesson),
              }))
            : [],
        emptyMessage: diagnosticsSummary.detail,
      },
      summary: {
        page: "Estrutura invalida",
        section: diagnosticsSummary.sectionLabel,
        discipline: plan.courseName || "--",
        lessons: String(plan.lessonCount),
        videos: "0",
        downloads: "0/0",
      },
      metrics,
    };
  }

  if (diagnostics.status !== "videos-found") {
    return {
      kind: "empty",
      presentation: {
        title: diagnosticsSummary.title,
        detail: diagnosticsSummary.detail,
        instructionsTitle: "Como interpretar",
        instructions: buildDiagnosticsInstructions(diagnostics, diagnosticsSummary, loading),
      },
      preview: {
        title: plan.courseName || "Central de Midia",
        items: plan.lessons.map((lesson) => ({
          badge: lesson.label.toUpperCase(),
          badgeClass: "badge-gray",
          name: lesson.displayTitle,
          sub: buildLessonPreviewSubtext(lesson),
        })),
        emptyMessage: diagnosticsSummary.detail,
      },
      summary: {
        page: "Valida",
        section: diagnosticsSummary.sectionLabel,
        discipline: plan.courseName || "--",
        lessons: String(plan.lessonCount),
        videos: "0",
        downloads: "0/0",
      },
      metrics,
    };
  }

  return {
    kind: "ready",
    plan,
    presentation: {
      title: "Curso por pasta pronto",
      detail: `${diagnosticsSummary.detail} ${plan.courseName} pronto para Downloads.`,
      instructionsTitle: "Proximo passo",
      instructions: buildReadyInstructions(connection, [
        `Os arquivos vao para Downloads/${plan.courseFolderName}/Aula 01/videos/...`,
        "Se quiser, gere tambem o arquivo complementar studyhub-course.json.",
        "Depois voce pode mover a pasta manualmente e seleciona-la no StudyHub.",
      ]),
    },
    preview: {
      title: plan.courseName,
      items: plan.lessons.map((lesson) => ({
        badge: lesson.label.toUpperCase(),
        badgeClass: "badge-blue",
        name: lesson.displayTitle,
        sub: buildLessonPreviewSubtext(lesson),
      })),
    },
    summary: buildSummaryFromPlan(plan, 0),
    metrics,
  };
}

function buildReadyInstructions(connection, instructions) {
  return connection?.reinjected
    ? ["A pagina precisou de reinjecao automatica antes da leitura.", ...instructions]
    : instructions;
}

function buildMetricsFromDiagnostics(diagnostics) {
  return {
    blocks: String(diagnostics.downloadBlocksProcessed || diagnostics.downloadBlocksDetected || 0),
    videoSections: String(diagnostics.videoSectionBlocksDetected || diagnostics.downloadSectionsDetected || 0),
    anchors: String(
      diagnostics.videoDownloadAnchorsProcessed || diagnostics.anchorsProcessed || diagnostics.directSectionAnchorsFound || 0
    ),
    mp4: String(diagnostics.validVideoLinksFound || 0),
    libras: String(diagnostics.ignoredByType?.["video-libras"] || 0),
    audio: String(diagnostics.ignoredByType?.audio || 0),
  };
}

function buildFallbackDiagnostics(courseData) {
  return {
    status: "structure-unrecognized",
    lessonHeadersDetected: Array.isArray(courseData.lessons) ? courseData.lessons.length : 0,
    lessonBlocksDetected: Array.isArray(courseData.lessons) ? courseData.lessons.length : 0,
    lessonsWithProcessedBlocks: 0,
    downloadBlocksProcessed: 0,
    videoSectionBlocksDetected: 0,
    downloadSectionsDetected: 0,
    videoDownloadAnchorsProcessed: 0,
    anchorsProcessed: 0,
    validVideoLinksFound: 0,
    ignoredLinksFound: 0,
    ignoredByType: {
      "video-libras": 0,
      audio: 0,
      material: 0,
      other: 0,
    },
    selectors: {
      blockSelector: "div.lnk-download[data-downid]",
      labelSelector: "div.lnk-download[data-downid] p",
      listSelector: "div.lnk-download[data-downid] ul",
      anchorSelector: "div.lnk-download[data-downid] ul a[href]",
    },
    notes: [],
  };
}

function buildDiagnosticsSummary(diagnostics) {
  switch (diagnostics.status) {
    case "videos-found":
      return {
        title: "Videos encontrados",
        detail:
          `${diagnostics.videoSectionBlocksDetected || diagnostics.downloadSectionsDetected} secao(oes) 'Video para download' detectada(s), ` +
          `${diagnostics.videoDownloadAnchorsProcessed || diagnostics.anchorsProcessed} link(s) direto(s) lido(s) da lista e ` +
          `${diagnostics.validVideoLinksFound} href(s) .mp4 aceito(s) como midia direta.`,
        sectionLabel: "Detectada",
      };
    case "lesson-grouping-error":
      return {
        title: "Agrupamento de aulas inconsistente",
        detail:
          `${diagnostics.lessonHeadersDetected} heading(s) de aula e ` +
          `${diagnostics.downloadBlocksProcessed} bloco(s) foram detectados, ` +
          `mas apenas ${diagnostics.lessonsWithProcessedBlocks || diagnostics.lessonBlocksDetected} aula(s) receberam conteudo.`,
        sectionLabel: "Inconsistente",
      };
    case "download-section-missing":
      return {
        title: "Secao de videos ausente",
        detail:
          "A Central de Midia foi reconhecida, mas nenhum bloco div.lnk-download[data-downid] com 'Video para download' apareceu.",
        sectionLabel: "Ausente",
      };
    case "download-section-without-links":
      return {
        title: "Secao detectada sem links",
        detail:
          "A secao 'Video para download' foi detectada, mas a lista <ul> associada ao <p> nao trouxe anchors a[href] validos.",
        sectionLabel: "Sem links",
      };
    case "download-section-without-valid-videos":
      return {
        title: "Secao detectada sem .mp4 valido",
        detail:
          "A lista da secao foi lida, mas nenhum href direto .mp4 foi aceito como video principal. Isso costuma indicar seletor incorreto da lista ou filtro de extensao agressivo.",
        sectionLabel: "Sem videos",
      };
    default:
      return {
        title: "Estrutura nao reconhecida",
        detail: "A Central de Midia nao apresentou uma estrutura de aula suficiente para a captura.",
        sectionLabel: "Indefinida",
      };
  }
}

function buildDiagnosticsInstructions(diagnostics, diagnosticsSummary, loading) {
  if (loading) {
    return [
      "Aguarde o carregamento terminar e tente novamente.",
      "Se houver conteudo em acordeons, deixe as aulas abertas antes de reescanear.",
    ];
  }

  const instructions = [diagnosticsSummary.detail];

  if (Array.isArray(diagnostics.notes) && diagnostics.notes.length > 0) {
    instructions.push(...diagnostics.notes);
  }

  if (diagnostics.selectors?.blockSelector) {
    instructions.push(`Bloco esperado: ${diagnostics.selectors.blockSelector}.`);
  }

  if (diagnostics.selectors?.anchorSelector) {
    instructions.push(`Anchors esperados: ${diagnostics.selectors.anchorSelector}.`);
  }

  if (diagnostics.selectors?.listSelector) {
    instructions.push(`Lista esperada: ${diagnostics.selectors.listSelector}.`);
  }

  if (diagnostics.status === "lesson-grouping-error") {
    instructions.push("Os blocos lnk-download precisam ser associados pela ordem global do DOM entre Aula 1, Aula 2, Aula 3 e assim por diante.");
  }

  instructions.push(
    "Se a pagina visualmente mostra os links, confirme se eles continuam na <ul> logo abaixo do <p> 'Video para download' dentro do bloco lnk-download."
  );
  instructions.push(
    "Quando a URL abre o .mp4 no navegador, isso continua sendo uma midia valida para a extensao; nao e necessario download automatico no clique."
  );
  return instructions;
}

function buildLessonPreviewSubtext(lesson) {
  const sectionText = lesson.sectionLabels.join(", ") || "sem secoes";
  const ignoredCounts = lesson.ignoredCounts || {};
  const ignoredPieces = [];

  if (ignoredCounts["video-libras"] > 0) {
    ignoredPieces.push(`libras ${ignoredCounts["video-libras"]}`);
  }

  if (ignoredCounts.audio > 0) {
    ignoredPieces.push(`audio ${ignoredCounts.audio}`);
  }

  if (ignoredCounts.material > 0) {
    ignoredPieces.push(`material ${ignoredCounts.material}`);
  }

  const blockCount = Number(lesson.blockCount || 0);
  const anchorCount = Number(lesson.anchorsProcessed || 0);
  const lead = `${lesson.videoCount} .mp4 | blocos ${blockCount} | anchors ${anchorCount}`;

  return ignoredPieces.length > 0
    ? `${lead} | ${sectionText} | ignorados: ${ignoredPieces.join(", ")}`
    : `${lead} | ${sectionText}`;
}

function buildCoursePlan(courseData, tab, diagnosticsSummary) {
  const courseName = normalizeText(courseData.disciplineName || "Disciplina");
  const courseFolderName = sanitizePathSegment(courseName, { separator: " ", fallback: "Disciplina" });
  const lessons = (courseData.lessons || []).map((lesson, index) =>
    buildLessonPlan(lesson, index, courseFolderName)
  );
  const videoFiles = lessons.flatMap((lesson) => lesson.files);

  return {
    courseName,
    courseFolderName,
    sectionStatusLabel: diagnosticsSummary.sectionLabel,
    diagnostics: courseData.diagnostics || buildFallbackDiagnostics(courseData),
    sourceUrl: tab?.url || courseData.url || null,
    lessons,
    videoFiles,
    metadataFile: {
      kind: "metadata",
      relativePath: joinRelativePath(courseFolderName, "studyhub-course.json"),
      payload: buildMetadataPayload({
        courseName,
        courseFolderName,
        lessons,
        videoFiles,
        sourceUrl: tab?.url || courseData.url || null,
      }),
    },
    lessonCount: lessons.length,
    videoCount: videoFiles.length,
  };
}

function buildLessonPlan(lesson, index, courseFolderName) {
  const order = Number(lesson.order || index + 1);
  const label = `Aula ${padNumber(order)}`;
  const sectionLabels = (lesson.sections || [])
    .filter((section) => section?.found)
    .map((section) => section.label);
  const lessonDiagnostics = lesson.diagnostics || {};
  const ignoredCounts = lessonDiagnostics.ignoredByType || {};
  const counters = { video: 0, pratica: 0, material: 0 };
  const files = (lesson.downloadLinks || []).map((link) =>
    buildVideoPlan(link, lesson, order, label, courseFolderName, counters)
  );

  return {
    order,
    label,
    displayTitle: normalizeText(lesson.displayTitle || label),
    sectionLabels,
    ignoredCounts,
    blockCount: Number(lessonDiagnostics.blockCount || 0),
    anchorsProcessed: Number(lessonDiagnostics.anchorsProcessed || 0),
    files,
    videoCount: files.length,
  };
}

function buildVideoPlan(link, lesson, order, lessonFolder, courseFolderName, counters) {
  const kind = classifyVideoKind(link);
  const titleSegment = sanitizePathSegment(cleanupVideoTitle(link.label || link.contextText || "Video"), {
    separator: "-",
    fallback: "",
    lowerCase: true,
  });
  const extension = detectFileExtension(link.url, link.label);
  let baseName = "";

  if (kind === "pratica") {
    counters.pratica += 1;
    baseName = `pratica-${padNumber(counters.pratica)}`;
  } else if (kind === "material") {
    counters.material += 1;
    baseName = titleSegment
      ? `material-${padNumber(counters.material)}-${titleSegment}`
      : `material-${padNumber(counters.material)}`;
  } else {
    counters.video += 1;
    baseName = titleSegment
      ? `video-${padNumber(counters.video)}-${titleSegment}`
      : `video-${padNumber(counters.video)}`;
  }

  return {
    kind: "video",
    lessonOrder: order,
    lessonLabel: lessonFolder,
    lessonDisplayTitle: normalizeText(lesson.displayTitle || lessonFolder),
    sectionKind: link.sectionKind || "other",
    originalName: normalizeText(link.label || link.contextText || "Video"),
    normalizedName: `${baseName}${extension}`,
    relativePath: joinRelativePath(courseFolderName, lessonFolder, "videos", `${baseName}${extension}`),
    sourceUrl: link.url,
  };
}

function classifyVideoKind(link) {
  const comparable = normalizeComparableText(
    `${link.label || ""} ${link.contextText || ""} ${link.sectionLabel || ""}`
  );

  if (comparable.includes("pratica")) {
    return "pratica";
  }

  if (comparable.includes("material")) {
    return "material";
  }

  return "video";
}

function cleanupVideoTitle(value) {
  return normalizeText(value)
    .replace(/^videos?\s*\d+\s*[:.-]?\s*/i, "")
    .replace(/^download\s*(do|da)?\s*video\s*[:.-]?\s*/i, "")
    .replace(/^aula\s+teorica\s*[:.-]?\s*/i, "")
    .replace(/^aula\s+pratica\s*[:.-]?\s*/i, "")
    .replace(/^material\s+escrito\s*[:.-]?\s*/i, "")
    .trim();
}

function detectFileExtension(url, label) {
  const match = `${url || ""} ${label || ""}`.match(/\.(mp4|m4v|mov|mkv|webm|avi)\b/i);
  return match ? `.${match[1].toLowerCase()}` : ".mp4";
}

function buildMetadataPayload(plan) {
  return {
    schemaVersion: "1.0.0",
    source: {
      kind: "browser-extension-download-plan",
      system: "studyhub-sync",
      provider: "univirtus",
      exportedAt: new Date().toISOString(),
      originUrl: plan.sourceUrl,
      pageType: "central-media",
    },
    course: {
      title: plan.courseName,
      folderName: plan.courseFolderName,
      mode: "folder-course",
    },
    summary: {
      lessonsDetected: plan.lessons.length,
      videosDetected: plan.videoFiles.length,
    },
    lessons: plan.lessons.map((lesson) => ({
      order: lesson.order,
      label: lesson.label,
      displayTitle: lesson.displayTitle,
      sections: lesson.sectionLabels,
      downloads: lesson.files.map((file) => ({
        originalName: file.originalName,
        normalizedName: file.normalizedName,
        relativePath: file.relativePath,
        sourceUrl: file.sourceUrl,
      })),
    })),
  };
}

function buildDownloadQueue(plan, includeMetadata) {
  return includeMetadata ? [...plan.videoFiles, plan.metadataFile] : [...plan.videoFiles];
}

async function downloadQueueItem(item) {
  if (item.kind === "metadata") {
    return downloadJson(item.payload, item.relativePath);
  }

  return downloadUrl(item.sourceUrl, item.relativePath);
}

async function downloadUrl(url, filename) {
  const downloadId = await withTimeout(
    chrome.downloads.download({
      url,
      filename,
      saveAs: false,
      conflictAction: "uniquify",
    }),
    TIMEOUTS.DOWNLOAD_MS,
    createPopupError("timeout", {
      state: POPUP_STATES.DOWNLOAD_ERROR,
      title: "Tempo esgotado ao iniciar download",
      detail: `O navegador demorou demais para aceitar ${filename}.`,
      instructionsTitle: "Como corrigir",
      instructions: [
        "Tente novamente em alguns segundos.",
        "Se a sessao da plataforma expirou, recarregue a pagina antes de repetir o fluxo.",
      ],
    })
  );

  if (typeof downloadId !== "number") {
    throw createPopupError("download-invalid-id", {
      state: POPUP_STATES.DOWNLOAD_ERROR,
      title: "Falha ao iniciar download",
      detail: `O navegador nao retornou um identificador valido para ${filename}.`,
      instructionsTitle: "Como corrigir",
      instructions: [
        "Confira se o navegador esta permitindo downloads para a extensao.",
        "Tente novamente apos atualizar a pagina da Central de Midia.",
      ],
    });
  }

  return downloadId;
}

function downloadJson(payload, filename) {
  const dataUrl = `data:application/json;charset=utf-8,${encodeURIComponent(
    JSON.stringify(payload, null, 2)
  )}`;

  return downloadUrl(dataUrl, filename);
}

function renderAnalysis(analysis) {
  renderSummary(analysis.summary || DEFAULT_SUMMARY);
  renderMetrics(analysis.metrics || null);
  renderPreview(analysis.preview || null);

  if (analysis.kind === "ready") {
    runtimeState.plan = analysis.plan;
    runtimeState.downloadsStarted = 0;
    applyState(POPUP_STATES.READY, analysis.presentation);
    return;
  }

  runtimeState.plan = null;
  runtimeState.downloadsStarted = 0;
  applyState(
    analysis.kind === "empty" ? POPUP_STATES.EMPTY : POPUP_STATES.INVALID_PAGE,
    analysis.presentation
  );
}

function renderSummary(summary) {
  const safe = summary || DEFAULT_SUMMARY;
  elements.summaryPage.textContent = safe.page;
  elements.summarySection.textContent = safe.section;
  elements.summaryDiscipline.textContent = safe.discipline;
  elements.summaryLessons.textContent = safe.lessons;
  elements.summaryVideos.textContent = safe.videos;
  elements.summaryDownloads.textContent = safe.downloads;
}

function renderMetrics(metrics) {
  if (!metrics) {
    elements.metricsArea.classList.add("is-hidden");
    elements.metricsBlocks.textContent = DEFAULT_METRICS.blocks;
    elements.metricsVideoSections.textContent = DEFAULT_METRICS.videoSections;
    elements.metricsAnchors.textContent = DEFAULT_METRICS.anchors;
    elements.metricsMp4.textContent = DEFAULT_METRICS.mp4;
    elements.metricsLibras.textContent = DEFAULT_METRICS.libras;
    elements.metricsAudio.textContent = DEFAULT_METRICS.audio;
    return;
  }

  elements.metricsBlocks.textContent = metrics.blocks;
  elements.metricsVideoSections.textContent = metrics.videoSections;
  elements.metricsAnchors.textContent = metrics.anchors;
  elements.metricsMp4.textContent = metrics.mp4;
  elements.metricsLibras.textContent = metrics.libras;
  elements.metricsAudio.textContent = metrics.audio;
  elements.metricsArea.classList.remove("is-hidden");
}

function renderPreview(preview) {
  elements.previewList.replaceChildren();

  if (!preview) {
    elements.previewArea.classList.add("is-hidden");
    return;
  }

  elements.previewTitle.textContent = preview.title;

  if (!Array.isArray(preview.items) || preview.items.length === 0) {
    elements.previewList.appendChild(
      createTextNode("div", "preview-empty", preview.emptyMessage || "Nenhum item encontrado.")
    );
    elements.previewArea.classList.remove("is-hidden");
    return;
  }

  preview.items.slice(0, 6).forEach((item) => {
    const row = document.createElement("div");
    row.className = "preview-item";

    const badge = createTextNode("span", `badge ${item.badgeClass || "badge-gray"}`, item.badge || "ITEM");
    const content = createTextNode("div", "preview-name", item.name || "");

    if (item.sub) {
      content.appendChild(createTextNode("div", "preview-sub", item.sub));
    }

    row.appendChild(badge);
    row.appendChild(content);
    elements.previewList.appendChild(row);
  });

  if (preview.items.length > 6) {
    elements.previewList.appendChild(
      createTextNode("div", "preview-empty", `... e mais ${preview.items.length - 6} item(ns)`)
    );
  }

  elements.previewArea.classList.remove("is-hidden");
}

function applyState(state, options = {}) {
  runtimeState.state = state;
  elements.status.dataset.state = state;
  elements.status.className = `status ${getStatusClass(state)}`;
  elements.status.setAttribute("aria-busy", isBusyState(state) ? "true" : "false");
  elements.statusTitle.textContent = options.title || getDefaultTitle(state);
  elements.statusDetail.textContent = options.detail || "";
  renderInstructions(options.instructionsTitle, options.instructions || []);
  updateActionButtons();
}

function renderInstructions(title, instructions) {
  elements.instructionsList.replaceChildren();

  if (!instructions.length) {
    elements.instructions.classList.add("is-hidden");
    elements.instructionsTitle.textContent = "";
    return;
  }

  elements.instructionsTitle.textContent = title || "Proximo passo";
  instructions.forEach((instruction) => {
    elements.instructionsList.appendChild(createTextNode("li", "", instruction));
  });
  elements.instructions.classList.remove("is-hidden");
}

function updateActionButtons() {
  elements.scanButton.disabled = runtimeState.isScanning || runtimeState.isDownloading;
  elements.downloadButton.disabled =
    runtimeState.isScanning || runtimeState.isDownloading || !runtimeState.plan;

  elements.scanButton.textContent =
    runtimeState.state === POPUP_STATES.CHECKING_PAGE
      ? DEFAULT_BUTTON_TEXT.checking
      : runtimeState.state === POPUP_STATES.CONNECTING
        ? DEFAULT_BUTTON_TEXT.connecting
        : runtimeState.state === POPUP_STATES.EXTRACTING
          ? DEFAULT_BUTTON_TEXT.extracting
          : DEFAULT_BUTTON_TEXT.scan;

  elements.downloadButton.textContent =
    runtimeState.state === POPUP_STATES.DOWNLOADING
      ? DEFAULT_BUTTON_TEXT.downloading
      : DEFAULT_BUTTON_TEXT.download;
}

function handlePopupError(error) {
  const popupError = normalizePopupError(error);

  if (popupError.state === POPUP_STATES.DOWNLOAD_ERROR && runtimeState.plan) {
    renderSummary(buildSummaryFromPlan(runtimeState.plan, runtimeState.downloadsStarted));
  } else {
    runtimeState.plan = null;
    runtimeState.downloadsStarted = 0;
    renderSummary(DEFAULT_SUMMARY);
    renderMetrics(null);
    renderPreview(null);
  }

  applyState(popupError.state, {
    title: popupError.title,
    detail: popupError.detail,
    instructionsTitle: popupError.instructionsTitle,
    instructions: popupError.instructions,
  });
}

function normalizePopupError(error) {
  if (error?.__studyhubPopupError) {
    return error;
  }

  return createPopupError("unexpected-error", {
    state: POPUP_STATES.COMMUNICATION_ERROR,
    title: "Erro inesperado",
    detail: error?.message || "Ocorreu uma falha inesperada durante a operacao.",
    instructionsTitle: "Como corrigir",
    instructions: [
      "Tente novamente em alguns segundos.",
      "Se a falha continuar, recarregue a aba do Univirtus antes de abrir o popup outra vez.",
    ],
  });
}

function createPopupError(code, options) {
  const error = new Error(options.detail || options.title || code);
  error.__studyhubPopupError = true;
  error.code = code;
  error.state = options.state;
  error.title = options.title;
  error.detail = options.detail;
  error.instructionsTitle = options.instructionsTitle || "Como corrigir";
  error.instructions = options.instructions || [];
  return error;
}

async function getActiveTab() {
  const tabs = await withTimeout(
    chrome.tabs.query({ active: true, currentWindow: true }),
    TIMEOUTS.TAB_QUERY_MS,
    createPopupError("timeout", {
      state: POPUP_STATES.COMMUNICATION_ERROR,
      title: "Tempo esgotado ao ler a aba",
      detail: "Nao foi possivel identificar a aba ativa a tempo.",
      instructionsTitle: "Como corrigir",
      instructions: [
        "Feche e abra o popup novamente.",
        "Confirme se existe uma aba web valida selecionada na janela atual.",
      ],
    })
  );

  const [tab] = tabs || [];

  if (!tab?.id) {
    throw createPopupError("active-tab-not-found", {
      state: POPUP_STATES.INVALID_PAGE,
      title: "Nenhuma aba valida encontrada",
      detail: "O popup nao encontrou uma aba ativa pronta para leitura.",
      instructionsTitle: "Como corrigir",
      instructions: [
        "Selecione uma aba do Univirtus.",
        "Evite abrir o popup em paginas internas do navegador.",
      ],
    });
  }

  return tab;
}

function validateActiveTab(tab) {
  if (!tab.url || !/^https?:\/\//i.test(String(tab.url))) {
    return createPopupError("page-invalid", {
      state: POPUP_STATES.INVALID_PAGE,
      title: "Pagina invalida para captura",
      detail: "O popup so funciona em paginas web comuns.",
      instructionsTitle: "Como corrigir",
      instructions: [
        "Abra a Central de Midia em uma aba comum do navegador.",
        "Depois disso, abra novamente o popup da extensao.",
      ],
    });
  }

  if (!isSupportedPageUrl(tab.url)) {
    return createPopupError("domain-incorrect", {
      state: POPUP_STATES.INVALID_PAGE,
      title: "Dominio incorreto",
      detail: "A aba ativa nao pertence a um dominio suportado do Univirtus ou Uninter.",
      instructionsTitle: "Paginas suportadas",
      instructions: [
        "Abra a disciplina correta no Univirtus.",
        "Depois navegue ate a Central de Midia antes de escanear.",
      ],
    });
  }

  return null;
}

async function sendTabMessage(tabId, message, timeoutMs) {
  try {
    return await withTimeout(
      chrome.tabs.sendMessage(tabId, message),
      timeoutMs,
      createPopupError("timeout", {
        state: POPUP_STATES.COMMUNICATION_ERROR,
        title: "Tempo esgotado na comunicacao",
        detail: "A pagina demorou demais para responder ao popup.",
        instructionsTitle: "Como corrigir",
        instructions: [
          "Espere a pagina terminar de carregar e tente novamente.",
          "Se o problema continuar, recarregue a aba antes de repetir a captura.",
        ],
      })
    );
  } catch (error) {
    throw createPopupError("communication-failed", {
      state: POPUP_STATES.COMMUNICATION_ERROR,
      title: "Falha de comunicacao com a pagina",
      detail: String(error?.message || error || "").trim() || "A pagina nao respondeu ao popup.",
      instructionsTitle: "Como corrigir",
      instructions: [
        "Tente escanear novamente.",
        "Se a falha continuar, recarregue a Central de Midia e abra o popup outra vez.",
      ],
    });
  }
}

function withTimeout(promise, timeoutMs, timeoutError) {
  let timeoutId = null;

  return new Promise((resolve, reject) => {
    timeoutId = window.setTimeout(() => reject(timeoutError), timeoutMs);

    Promise.resolve(promise)
      .then((value) => {
        if (timeoutId !== null) {
          window.clearTimeout(timeoutId);
        }
        resolve(value);
      })
      .catch((error) => {
        if (timeoutId !== null) {
          window.clearTimeout(timeoutId);
        }
        reject(error);
      });
  });
}

function buildSummaryFromPlan(plan, downloadsStarted) {
  return {
    page: "Valida",
    section: plan.sectionStatusLabel || "Detectada",
    discipline: plan.courseName,
    lessons: String(plan.lessonCount),
    videos: String(plan.videoCount),
    downloads: `${downloadsStarted}/${plan.videoCount}`,
  };
}

function buildDownloadDetail(plan, downloadsStarted, includeMetadata) {
  return (
    `${downloadsStarted} de ${plan.videoCount} video(s) ja foram enviados para a fila de Downloads` +
    `${includeMetadata ? " + studyhub-course.json." : "."}`
  );
}

function sanitizePathSegment(value, options = {}) {
  const separator = options.separator ?? "-";
  const fallback = options.fallback ?? "arquivo";
  const lowerCase = options.lowerCase ?? false;
  const pattern = separator === " " ? /\s+/g : /[\s_-]+/g;
  const cleaned = String(value || "")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[<>:"/\\|?*\u0000-\u001F]/g, " ")
    .replace(/[&]+/g, " e ")
    .replace(/[^\w\s.-]/g, " ")
    .replace(/[.]+/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .replace(pattern, separator)
    .replace(new RegExp(`^\\${separator}+|\\${separator}+$`, "g"), "");

  return (lowerCase ? cleaned.toLowerCase() : cleaned) || fallback;
}

function joinRelativePath(...parts) {
  return parts
    .filter(Boolean)
    .map((part) => String(part).replace(/\\/g, "/").replace(/^\/+|\/+$/g, ""))
    .join("/");
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

function padNumber(value) {
  return String(value).padStart(2, "0");
}

function isPageStillLoading(tab, meta) {
  return tab?.status === "loading" || meta?.readyState === "loading" || meta?.readyState === "interactive";
}

function isTimeoutError(error) {
  return Boolean(error?.__studyhubPopupError) && error.code === "timeout";
}

function getStatusClass(state) {
  switch (state) {
    case POPUP_STATES.CHECKING_PAGE:
    case POPUP_STATES.CONNECTING:
    case POPUP_STATES.EXTRACTING:
    case POPUP_STATES.DOWNLOADING:
      return "status-working";
    case POPUP_STATES.READY:
    case POPUP_STATES.DOWNLOAD_SUCCESS:
      return "status-success";
    case POPUP_STATES.EMPTY:
    case POPUP_STATES.INVALID_PAGE:
      return "status-warning";
    case POPUP_STATES.DOWNLOAD_ERROR:
    case POPUP_STATES.COMMUNICATION_ERROR:
      return "status-error";
    default:
      return "status-neutral";
  }
}

function isBusyState(state) {
  return (
    state === POPUP_STATES.CHECKING_PAGE ||
    state === POPUP_STATES.CONNECTING ||
    state === POPUP_STATES.EXTRACTING ||
    state === POPUP_STATES.DOWNLOADING
  );
}

function getDefaultTitle(state) {
  switch (state) {
    case POPUP_STATES.CHECKING_PAGE:
      return "Validando a aba ativa";
    case POPUP_STATES.CONNECTING:
      return "Conectando com a pagina";
    case POPUP_STATES.EXTRACTING:
      return "Extraindo a Central de Midia";
    case POPUP_STATES.EMPTY:
      return "Nada aproveitavel encontrado";
    case POPUP_STATES.READY:
      return "Curso por pasta pronto";
    case POPUP_STATES.DOWNLOADING:
      return "Iniciando downloads";
    case POPUP_STATES.DOWNLOAD_SUCCESS:
      return "Downloads iniciados";
    case POPUP_STATES.DOWNLOAD_ERROR:
      return "Falha ao iniciar downloads";
    case POPUP_STATES.INVALID_PAGE:
      return "Pagina invalida";
    case POPUP_STATES.COMMUNICATION_ERROR:
      return "Falha de comunicacao";
    default:
      return "Pronto para escanear";
  }
}

function isValidExtractResult(result) {
  return (
    isPlainObject(result) &&
    typeof result.type === "string" &&
    Object.prototype.hasOwnProperty.call(result, "data")
  );
}

function isPlainObject(value) {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isSupportedPageUrl(url) {
  try {
    const hostname = new URL(url).hostname.toLowerCase();
    return (
      hostname === "univirtus.com.br" ||
      hostname.endsWith(".univirtus.com.br") ||
      hostname === "uninter.com" ||
      hostname.endsWith(".uninter.com")
    );
  } catch {
    return false;
  }
}

function createTextNode(tagName, className, text) {
  const node = document.createElement(tagName);
  if (className) {
    node.className = className;
  }
  node.textContent = text;
  return node;
}
