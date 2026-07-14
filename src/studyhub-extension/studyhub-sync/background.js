const DOWNLOAD_MESSAGE_TARGET = "studyhub-downloads";
const STORAGE_JOB_KEY = "studyhubDownloadJob";
const MAX_CONCURRENT_DOWNLOADS = 2;

const JOB_STATUS = Object.freeze({
  QUEUED: "queued",
  RUNNING: "running",
  CANCELLING: "cancelling",
  COMPLETED: "completed",
  COMPLETED_WITH_ERRORS: "completed-with-errors",
  CANCELLED: "cancelled",
});

const ITEM_STATUS = Object.freeze({
  PENDING: "pending",
  STARTED: "started",
  COMPLETE: "complete",
  INTERRUPTED: "interrupted",
  CANCELLED: "cancelled",
});

let jobCache = null;
let mutationQueue = Promise.resolve();

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || message.target !== DOWNLOAD_MESSAGE_TARGET) {
    return false;
  }

  handleRuntimeMessage(message)
    .then((payload) => sendResponse({ ok: true, ...payload }))
    .catch((error) => {
      console.error("[StudyHub Sync] background message failed", error);
      sendResponse({ ok: false, error: serializeError(error) });
    });

  return true;
});

chrome.downloads.onChanged.addListener((delta) => {
  handleDownloadChanged(delta).catch((error) => {
    console.error("[StudyHub Sync] failed to process download change", error);
  });
});

chrome.runtime.onInstalled.addListener(() => {
  restoreActiveJob().catch((error) => {
    console.error("[StudyHub Sync] failed to restore job after install", error);
  });
});

chrome.runtime.onStartup.addListener(() => {
  restoreActiveJob().catch((error) => {
    console.error("[StudyHub Sync] failed to restore job on startup", error);
  });
});

async function handleRuntimeMessage(message) {
  switch (message.action) {
    case "START_JOB":
      return { job: await startJob(message.plan) };
    case "GET_JOB":
      return { job: await getCurrentJob() };
    case "CANCEL_JOB":
      return { job: await cancelJob(message.jobId) };
    default:
      throw createBackgroundError("unknown-action", "Acao desconhecida para a fila de downloads.");
  }
}

async function restoreActiveJob() {
  await runExclusive(async () => {
    const job = await loadJob();
    if (!isJobRunnable(job)) {
      return;
    }

    console.log("[StudyHub Sync] restoring active job " + job.id);
    await pumpQueue(job);
    await persistJob(job);
  });
}

async function startJob(plan) {
  return runExclusive(async () => {
    const existingJob = await loadJob();
    if (isJobActive(existingJob)) {
      throw createBackgroundError(
        "job-active",
        "Ja existe uma fila de downloads em andamento. Finalize ou cancele antes de iniciar outra."
      );
    }

    const job = createJob(plan);
    await persistJob(job);

    console.log(
      "[StudyHub Sync] job " +
        job.id +
        " started: " +
        job.counts.planned +
        " arquivo(s), " +
        job.counts.videos.planned +
        " video(s)."
    );

    await pumpQueue(job);
    await persistJob(job);
    return publicJob(job);
  });
}

async function getCurrentJob() {
  return runExclusive(async () => {
    const job = await loadJob();
    if (!job) {
      return null;
    }

    if (isJobRunnable(job)) {
      await pumpQueue(job);
      await persistJob(job);
    }

    return publicJob(job);
  });
}

async function cancelJob(jobId) {
  return runExclusive(async () => {
    const job = await loadJob();
    if (!job) {
      return null;
    }

    if (jobId && job.id !== jobId) {
      throw createBackgroundError("job-mismatch", "O job ativo nao corresponde ao pedido de cancelamento.");
    }

    if (isJobTerminal(job)) {
      return publicJob(job);
    }

    job.status = JOB_STATUS.CANCELLING;
    job.cancelRequestedAt = nowIso();

    for (const item of job.items) {
      if (item.status === ITEM_STATUS.PENDING) {
        item.status = ITEM_STATUS.CANCELLED;
        item.completedAt = nowIso();
      }
    }

    refreshJobState(job);
    await persistJob(job);

    const activeItems = job.items.filter(
      (item) => item.status === ITEM_STATUS.STARTED && typeof item.downloadId === "number"
    );

    for (const item of activeItems) {
      try {
        console.warn("[StudyHub Sync] cancelling download " + item.downloadId + " for " + item.relativePath);
        await cancelChromeDownload(item.downloadId);
      } catch (error) {
        markItemInterrupted(job, item, {
          code: "cancel-failed",
          reason: "cancel-failed",
          message: error && error.message ? error.message : "Falha ao cancelar download ativo.",
        });
      }
    }

    finishJobIfDone(job);
    await persistJob(job);
    return publicJob(job);
  });
}

async function handleDownloadChanged(delta) {
  if (!delta || typeof delta.id !== "number") {
    return;
  }

  await runExclusive(async () => {
    const job = await loadJob();
    if (!job || isJobTerminal(job)) {
      return;
    }

    const item = job.items.find((entry) => entry.downloadId === delta.id);
    if (!item) {
      return;
    }

    if (delta.filename && delta.filename.current) {
      item.browserFilename = delta.filename.current;
    }

    if (delta.error && delta.error.current) {
      item.lastBrowserError = delta.error.current;
    }

    if (delta.state && delta.state.current === "complete") {
      markItemComplete(job, item);
      console.log("[StudyHub Sync] download complete: " + item.relativePath);
    } else if (delta.state && delta.state.current === "interrupted") {
      const reason = (delta.error && delta.error.current) || item.lastBrowserError || "interrupted";
      markItemInterrupted(job, item, {
        code: "download-interrupted",
        reason,
        message: "Download interrompido pelo navegador: " + reason,
      });
      console.warn("[StudyHub Sync] download interrupted: " + item.relativePath + " (" + reason + ")");
    } else {
      await persistJob(job);
      return;
    }

    finishJobIfDone(job);

    if (isJobRunnable(job)) {
      await pumpQueue(job);
    }

    await persistJob(job);
  });
}

async function pumpQueue(job) {
  if (!isJobRunnable(job)) {
    return;
  }

  job.status = JOB_STATUS.RUNNING;
  refreshJobState(job);

  while (isJobRunnable(job) && job.activeFiles.length < job.concurrency) {
    const item = job.items.find((entry) => entry.status === ITEM_STATUS.PENDING);
    if (!item) {
      break;
    }

    try {
      console.log("[StudyHub Sync] starting download: " + item.relativePath);
      const downloadId = await startChromeDownload(item);
      item.status = ITEM_STATUS.STARTED;
      item.downloadId = downloadId;
      item.startedAt = nowIso();
      item.error = null;
      item.lastError = null;
      job.lastProcessedFile = buildFileSummary(item);
      job.latestActivity = {
        type: "started",
        at: item.startedAt,
        file: buildFileSummary(item),
      };
      console.log("[StudyHub Sync] download accepted #" + downloadId + ": " + item.relativePath);
    } catch (error) {
      markItemInterrupted(job, item, {
        code: "download-start-failed",
        reason: "start-failed",
        message: error && error.message ? error.message : "Falha ao iniciar download.",
      });
      console.error("[StudyHub Sync] failed to start download: " + item.relativePath, error);
    }

    refreshJobState(job);
    await persistJob(job);
  }

  finishJobIfDone(job);
  refreshJobState(job);
}

function createJob(plan) {
  const normalizedPlan = normalizePlan(plan);
  const createdAt = nowIso();
  const items = normalizedPlan.items.map((item, index) => ({
    id: item.id || "item-" + String(index + 1).padStart(3, "0"),
    index,
    kind: item.kind,
    status: ITEM_STATUS.PENDING,
    sourceUrl: item.sourceUrl || null,
    payload: item.payload || null,
    relativePath: item.relativePath,
    lessonLabel: item.lessonLabel || null,
    lessonOrder: item.lessonOrder || null,
    originalName: item.originalName || item.normalizedName || getNameFromPath(item.relativePath),
    normalizedName: item.normalizedName || getNameFromPath(item.relativePath),
    sectionKind: item.sectionKind || null,
    downloadId: null,
    error: null,
    lastBrowserError: null,
    startedAt: null,
    completedAt: null,
  }));

  const job = {
    version: 1,
    id: "studyhub-" + Date.now() + "-" + Math.random().toString(36).slice(2, 8),
    status: JOB_STATUS.QUEUED,
    createdAt,
    updatedAt: createdAt,
    finishedAt: null,
    cancelRequestedAt: null,
    concurrency: MAX_CONCURRENT_DOWNLOADS,
    source: normalizedPlan.source || "popup",
    courseName: normalizedPlan.courseName,
    courseFolderName: normalizedPlan.courseFolderName,
    sourceUrl: normalizedPlan.sourceUrl || null,
    sectionStatusLabel: normalizedPlan.sectionStatusLabel || "Detectada",
    includeMetadata: Boolean(normalizedPlan.includeMetadata),
    lessonCount: normalizedPlan.lessonCount,
    videoCount: normalizedPlan.videoCount,
    items,
    pendingFiles: [],
    startedFiles: [],
    activeFiles: [],
    completedFiles: [],
    interruptedFiles: [],
    cancelledFiles: [],
    errors: [],
    counts: null,
    latestActivity: null,
    lastProcessedFile: null,
  };

  refreshJobState(job);
  return job;
}

function normalizePlan(plan) {
  if (!isPlainObject(plan)) {
    throw createBackgroundError("invalid-plan", "Plano de download invalido.");
  }

  const items = Array.isArray(plan.items) ? plan.items.map(normalizeItem) : [];
  if (items.length === 0) {
    throw createBackgroundError("empty-plan", "O plano de download nao possui arquivos.");
  }

  return {
    source: safeString(plan.source),
    courseName: safeString(plan.courseName) || "Disciplina",
    courseFolderName: safeString(plan.courseFolderName) || "Disciplina",
    sourceUrl: safeString(plan.sourceUrl),
    sectionStatusLabel: safeString(plan.sectionStatusLabel) || "Detectada",
    includeMetadata: Boolean(plan.includeMetadata),
    lessonCount: Number(plan.lessonCount || 0),
    videoCount: Number(plan.videoCount || items.filter((item) => item.kind === "video").length),
    items,
  };
}

function normalizeItem(item, index) {
  if (!isPlainObject(item)) {
    throw createBackgroundError("invalid-item", "Item de download invalido.");
  }

  const kind = item.kind === "metadata" ? "metadata" : "video";
  const relativePath = safeString(item.relativePath);
  if (!relativePath) {
    throw createBackgroundError("invalid-path", "Item de download sem caminho relativo.");
  }

  if (kind === "video" && !safeString(item.sourceUrl)) {
    throw createBackgroundError("invalid-url", "Video sem URL de origem: " + relativePath);
  }

  return {
    id: safeString(item.id) || "item-" + String(index + 1).padStart(3, "0"),
    kind,
    sourceUrl: safeString(item.sourceUrl),
    payload: isPlainObject(item.payload) || Array.isArray(item.payload) ? cloneSerializable(item.payload) : item.payload || null,
    relativePath,
    lessonLabel: safeString(item.lessonLabel),
    lessonOrder: item.lessonOrder === null || item.lessonOrder === undefined ? null : Number(item.lessonOrder),
    originalName: safeString(item.originalName),
    normalizedName: safeString(item.normalizedName),
    sectionKind: safeString(item.sectionKind),
  };
}

function markItemComplete(job, item) {
  item.status = ITEM_STATUS.COMPLETE;
  item.completedAt = nowIso();
  item.error = null;
  job.lastProcessedFile = buildFileSummary(item);
  job.latestActivity = {
    type: "complete",
    at: item.completedAt,
    file: buildFileSummary(item),
  };
  refreshJobState(job);
}

function markItemInterrupted(job, item, errorInfo) {
  item.status = ITEM_STATUS.INTERRUPTED;
  item.completedAt = nowIso();
  item.error = {
    itemId: item.id,
    downloadId: typeof item.downloadId === "number" ? item.downloadId : null,
    code: errorInfo.code || "download-error",
    reason: errorInfo.reason || "unknown",
    message: errorInfo.message || "Falha no download.",
    lessonLabel: item.lessonLabel,
    lessonOrder: item.lessonOrder,
    name: item.normalizedName || item.originalName || getNameFromPath(item.relativePath),
    originalName: item.originalName,
    relativePath: item.relativePath,
    kind: item.kind,
    at: item.completedAt,
  };
  job.lastProcessedFile = buildFileSummary(item);
  job.latestActivity = {
    type: "interrupted",
    at: item.completedAt,
    file: buildFileSummary(item),
    error: item.error,
  };
  refreshJobState(job);
}

function finishJobIfDone(job) {
  refreshJobState(job);

  if (isJobTerminal(job)) {
    return;
  }

  const hasPending = job.pendingFiles.length > 0;
  const hasActive = job.activeFiles.length > 0;

  if (job.status === JOB_STATUS.CANCELLING && !hasActive) {
    job.status = JOB_STATUS.CANCELLED;
    job.finishedAt = nowIso();
    logJobSummary(job);
    return;
  }

  if (!hasPending && !hasActive) {
    job.status = job.interruptedFiles.length > 0 ? JOB_STATUS.COMPLETED_WITH_ERRORS : JOB_STATUS.COMPLETED;
    job.finishedAt = nowIso();
    logJobSummary(job);
  }
}

function refreshJobState(job) {
  if (!job || !Array.isArray(job.items)) {
    return;
  }

  const pendingFiles = [];
  const startedFiles = [];
  const activeFiles = [];
  const completedFiles = [];
  const interruptedFiles = [];
  const cancelledFiles = [];
  const errors = [];

  for (const item of job.items) {
    const summary = buildFileSummary(item);
    if (item.status === ITEM_STATUS.PENDING) {
      pendingFiles.push(summary);
    }
    if (typeof item.downloadId === "number") {
      startedFiles.push(summary);
    }
    if (item.status === ITEM_STATUS.STARTED) {
      activeFiles.push(summary);
    }
    if (item.status === ITEM_STATUS.COMPLETE) {
      completedFiles.push(summary);
    }
    if (item.status === ITEM_STATUS.INTERRUPTED) {
      interruptedFiles.push(summary);
    }
    if (item.status === ITEM_STATUS.CANCELLED) {
      cancelledFiles.push(summary);
    }
    if (item.error) {
      errors.push(cloneSerializable(item.error));
    }
  }

  const videoItems = job.items.filter((item) => item.kind === "video");
  job.pendingFiles = pendingFiles;
  job.startedFiles = startedFiles;
  job.activeFiles = activeFiles;
  job.completedFiles = completedFiles;
  job.interruptedFiles = interruptedFiles;
  job.cancelledFiles = cancelledFiles;
  job.errors = errors;
  job.counts = {
    planned: job.items.length,
    pending: pendingFiles.length,
    started: startedFiles.length,
    active: activeFiles.length,
    completed: completedFiles.length,
    interrupted: interruptedFiles.length,
    cancelled: cancelledFiles.length,
    failed: interruptedFiles.length,
    videos: {
      planned: videoItems.length,
      completed: videoItems.filter((item) => item.status === ITEM_STATUS.COMPLETE).length,
      interrupted: videoItems.filter((item) => item.status === ITEM_STATUS.INTERRUPTED).length,
      pending: videoItems.filter((item) => item.status === ITEM_STATUS.PENDING).length,
      active: videoItems.filter((item) => item.status === ITEM_STATUS.STARTED).length,
    },
  };
  job.updatedAt = nowIso();
}

function buildFileSummary(item) {
  return {
    itemId: item.id,
    kind: item.kind,
    status: item.status,
    downloadId: typeof item.downloadId === "number" ? item.downloadId : null,
    relativePath: item.relativePath,
    lessonLabel: item.lessonLabel,
    lessonOrder: item.lessonOrder,
    name: item.normalizedName || item.originalName || getNameFromPath(item.relativePath),
  };
}

async function startChromeDownload(item) {
  const url = item.kind === "metadata" ? createMetadataDataUrl(item.payload) : item.sourceUrl;
  if (!url) {
    throw createBackgroundError("missing-url", "Item sem URL: " + item.relativePath);
  }

  return new Promise((resolve, reject) => {
    try {
      chrome.downloads.download(
        {
          url,
          filename: item.relativePath,
          saveAs: false,
          conflictAction: "uniquify",
        },
        (downloadId) => {
          const lastError = chrome.runtime.lastError;
          if (lastError) {
            reject(new Error(lastError.message));
            return;
          }

          if (typeof downloadId !== "number") {
            reject(new Error("O navegador nao retornou um downloadId valido."));
            return;
          }

          resolve(downloadId);
        }
      );
    } catch (error) {
      reject(error);
    }
  });
}

async function cancelChromeDownload(downloadId) {
  return new Promise((resolve, reject) => {
    try {
      chrome.downloads.cancel(downloadId, () => {
        const lastError = chrome.runtime.lastError;
        if (lastError) {
          reject(new Error(lastError.message));
          return;
        }
        resolve();
      });
    } catch (error) {
      reject(error);
    }
  });
}

function createMetadataDataUrl(payload) {
  return "data:application/json;charset=utf-8," + encodeURIComponent(JSON.stringify(payload || {}, null, 2));
}

async function loadJob() {
  if (jobCache) {
    return cloneSerializable(jobCache);
  }

  const stored = await storageGet(STORAGE_JOB_KEY);
  jobCache = stored && stored[STORAGE_JOB_KEY] ? stored[STORAGE_JOB_KEY] : null;

  if (jobCache) {
    refreshJobState(jobCache);
  }

  return jobCache ? cloneSerializable(jobCache) : null;
}

async function persistJob(job) {
  refreshJobState(job);
  const snapshot = cloneSerializable(job);
  jobCache = snapshot;
  await storageSet({ [STORAGE_JOB_KEY]: snapshot });
  return snapshot;
}

function storageGet(key) {
  return new Promise((resolve, reject) => {
    chrome.storage.local.get(key, (result) => {
      const lastError = chrome.runtime.lastError;
      if (lastError) {
        reject(new Error(lastError.message));
        return;
      }
      resolve(result || {});
    });
  });
}

function storageSet(value) {
  return new Promise((resolve, reject) => {
    chrome.storage.local.set(value, () => {
      const lastError = chrome.runtime.lastError;
      if (lastError) {
        reject(new Error(lastError.message));
        return;
      }
      resolve();
    });
  });
}

function runExclusive(operation) {
  const run = mutationQueue.then(operation, operation);
  mutationQueue = run.catch(() => {});
  return run;
}

function isJobRunnable(job) {
  return Boolean(job && (job.status === JOB_STATUS.QUEUED || job.status === JOB_STATUS.RUNNING));
}

function isJobActive(job) {
  return Boolean(job && (isJobRunnable(job) || job.status === JOB_STATUS.CANCELLING));
}

function isJobTerminal(job) {
  return Boolean(
    job &&
      (job.status === JOB_STATUS.COMPLETED ||
        job.status === JOB_STATUS.COMPLETED_WITH_ERRORS ||
        job.status === JOB_STATUS.CANCELLED)
  );
}

function publicJob(job) {
  return job ? cloneSerializable(job) : null;
}

function cloneSerializable(value) {
  return JSON.parse(JSON.stringify(value));
}

function safeString(value) {
  return typeof value === "string" ? value.trim() : "";
}

function isPlainObject(value) {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function getNameFromPath(value) {
  return String(value || "").split("/").filter(Boolean).pop() || "arquivo";
}

function nowIso() {
  return new Date().toISOString();
}

function createBackgroundError(code, message) {
  const error = new Error(message);
  error.code = code;
  return error;
}

function serializeError(error) {
  return {
    code: error && error.code ? error.code : "background-error",
    message: error && error.message ? error.message : "Falha inesperada no service worker.",
  };
}

function logJobSummary(job) {
  refreshJobState(job);
  const counts = job.counts || {};
  const videoCounts = counts.videos || {};
  console.log(
    "[StudyHub Sync] job " +
      job.id +
      " finished as " +
      job.status +
      ": " +
      (counts.completed || 0) +
      " arquivo(s) concluidos, " +
      (counts.interrupted || 0) +
      " interrompido(s), " +
      (videoCounts.completed || 0) +
      "/" +
      (videoCounts.planned || 0) +
      " video(s)."
  );
}