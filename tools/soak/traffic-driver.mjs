import { SmartInspect } from '../../smartinspect-js/dist/smartinspect.esm.js';

function installBrowserShim() {
  if (typeof globalThis.window === 'undefined') {
    globalThis.window = {
      setTimeout: globalThis.setTimeout.bind(globalThis),
      clearTimeout: globalThis.clearTimeout.bind(globalThis),
      setInterval: globalThis.setInterval.bind(globalThis),
      clearInterval: globalThis.clearInterval.bind(globalThis),
      addEventListener: () => {},
      removeEventListener: () => {},
      location: { href: 'http://localhost/smartinspect-soak-driver' }
    };
  }

  if (typeof globalThis.document === 'undefined') {
    globalThis.document = {
      visibilityState: 'visible',
      addEventListener: () => {},
      removeEventListener: () => {}
    };
  }
}

function parseArgs(argv) {
  const values = new Map();
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (!arg.startsWith('--')) {
      continue;
    }

    const key = arg.slice(2);
    if (key === 'help') {
      values.set('help', 'true');
      continue;
    }

    values.set(key, argv[index + 1] ?? '');
    index += 1;
  }

  return values;
}

function getValue(values, key, defaultValue) {
  const value = values.get(key);
  return value && value.trim().length > 0 ? value : defaultValue;
}

function parseIntOption(values, key, defaultValue, minValue) {
  const parsed = Number.parseInt(getValue(values, key, String(defaultValue)), 10);
  if (Number.isNaN(parsed) || parsed < minValue) {
    throw new Error(`Expected --${key} to be an integer >= ${minValue}.`);
  }

  return parsed;
}

function parseOptions(argv) {
  const values = parseArgs(argv);
  if (values.has('help')) {
    return { showHelp: true };
  }

  const transport = getValue(values, 'transport', 'websocket');
  if (transport !== 'websocket' && transport !== 'http') {
    throw new Error(`Unsupported --transport '${transport}'. Expected websocket or http.`);
  }

  return {
    showHelp: false,
    transport,
    url: getValue(values, 'url', transport === 'http' ? 'http://127.0.0.1:5109/api/v1' : 'ws://127.0.0.1:4229'),
    durationSeconds: parseIntOption(values, 'duration-seconds', 300, 1),
    sessions: parseIntOption(values, 'sessions', 4, 1),
    messagesPerSecond: parseIntOption(values, 'messages-per-second', 500, 0),
    payloadBytes: parseIntOption(values, 'payload-bytes', 1024, 0),
    watchesEvery: parseIntOption(values, 'watches-every', 100, 0),
    flowsEvery: parseIntOption(values, 'flows-every', 200, 0),
    timeoutSeconds: parseIntOption(values, 'timeout-seconds', 30, 1),
    reconnectDelayMs: parseIntOption(values, 'reconnect-delay-ms', 2000, 0),
    maxReconnectAttempts: parseIntOption(values, 'max-reconnect-attempts', 0, 0),
    maxBufferSize: parseIntOption(values, 'max-buffer-size', 2000, 1),
    flushIntervalMs: parseIntOption(values, 'flush-interval-ms', 1000, 1),
    maxBatchSize: parseIntOption(values, 'max-batch-size', 50, 1),
    retryAttempts: parseIntOption(values, 'retry-attempts', 3, 0),
    retryBaseDelayMs: parseIntOption(values, 'retry-base-delay-ms', 1000, 0),
    retryMaxDelayMs: parseIntOption(values, 'retry-max-delay-ms', 30000, 1),
    appPrefix: getValue(values, 'app-prefix', 'SoakBrowser'),
    sessionPrefix: getValue(values, 'session-prefix', 'Session')
  };
}

function printHelp() {
  console.log(`
SmartInspect soak traffic driver

Options:
  --transport websocket|http   Driver mode. Default: websocket
  --url <value>                WebSocket or relay base URL.
  --duration-seconds <n>       Test duration. Default: 300
  --sessions <n>               Concurrent logical sessions. Default: 4
  --messages-per-second <n>    Per-session message rate. 0 = unthrottled. Default: 500
  --payload-bytes <n>          Approximate payload size. Default: 1024
  --watches-every <n>          Emit a watch every N logs. Default: 100
  --flows-every <n>            Emit a process flow message every N logs. Default: 200
  --timeout-seconds <n>        Fail if websocket stays disconnected longer than this. Default: 30
  --reconnect-delay-ms <n>     WebSocket reconnect delay. Default: 2000
  --max-reconnect-attempts <n> WebSocket reconnect attempts. 0 = infinite. Default: 0
  --max-buffer-size <n>        Client-side message buffer. Default: 2000
  --flush-interval-ms <n>      HTTP flush interval. Default: 1000
  --max-batch-size <n>         HTTP max batch size. Default: 50
  --retry-attempts <n>         HTTP retry attempts. Default: 3
  --retry-base-delay-ms <n>    HTTP retry base delay. Default: 1000
  --retry-max-delay-ms <n>     HTTP retry max delay. Default: 30000
  --app-prefix <text>          Application prefix. Default: SoakBrowser
  --session-prefix <text>      Session prefix. Default: Session
  --help                       Show this text
`.trim());
}

function createPayload(payloadBytes) {
  if (payloadBytes <= 0) {
    return undefined;
  }

  let payload = '{"message":"';
  while (payload.length < payloadBytes - 16) {
    payload += 'browser-soak-data-';
  }

  payload += '"}';
  return payload.length > payloadBytes ? payload.slice(0, payloadBytes) : payload;
}

function pickLogMethod(session, sequence) {
  const slot = sequence % 50;
  if (slot === 0) {
    return session.logError.bind(session);
  }

  if (slot === 1) {
    return session.logWarning.bind(session);
  }

  if (slot === 2) {
    return session.logDebug.bind(session);
  }

  if (slot === 3) {
    return session.logVerbose.bind(session);
  }

  return session.logMessage.bind(session);
}

function sleep(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

async function main() {
  installBrowserShim();

  const options = parseOptions(process.argv.slice(2));
  if (options.showHelp) {
    printHelp();
    return;
  }

  const stats = {
    logsSent: 0,
    watchesSent: 0,
    flowsSent: 0,
    errors: 0
  };

  const appName = `${options.appPrefix}-${process.pid}`;
  const smartInspect = new SmartInspect(appName, options.transport === 'http'
    ? {
        connectionType: 'http',
        httpOptions: {
          flushInterval: options.flushIntervalMs,
          maxBatchSize: options.maxBatchSize,
          maxBufferSize: options.maxBufferSize,
          retry: {
            maxAttempts: options.retryAttempts,
            baseDelay: options.retryBaseDelayMs,
            maxDelay: options.retryMaxDelayMs
          },
          includeMetadata: false
        }
      }
    : {
        autoReconnect: true,
        reconnectDelay: options.reconnectDelayMs,
        maxReconnectAttempts: options.maxReconnectAttempts,
        bufferWhenDisconnected: true,
        maxBufferSize: options.maxBufferSize
      });

  let currentState = 'disconnected';
  let disconnectedAt = Date.now();
  let connectedAtLeastOnce = false;

  smartInspect.events = {
    onStateChange: state => {
      currentState = state;
      if (state === 'connected') {
        connectedAtLeastOnce = true;
        disconnectedAt = 0;
      } else if (state === 'disconnected' || state === 'reconnecting') {
        disconnectedAt = Date.now();
      }

      console.log(`${new Date().toISOString()} | state=${state}`);
    },
    onError: error => {
      stats.errors += 1;
      console.error(`${new Date().toISOString()} | error=${error.message}`);
    }
  };

  try {
    await smartInspect.connect(options.url);
  } catch (error) {
    smartInspect.disconnect();
    throw error;
  }

  const sessions = Array.from({ length: options.sessions }, (_, index) =>
    smartInspect.addSession(`${options.sessionPrefix}-${String(index + 1).padStart(2, '0')}`));
  const payload = createPayload(options.payloadBytes);
  const stopAt = Date.now() + options.durationSeconds * 1000;
  let stopRequested = false;

  const reporter = setInterval(() => {
    console.log(
      `${new Date().toISOString()} | transport=${options.transport} | sessions=${options.sessions} | ` +
      `logs=${stats.logsSent} | watches=${stats.watchesSent} | flows=${stats.flowsSent} | ` +
      `state=${currentState} | errors=${stats.errors}`);
  }, 1000);

  const workers = sessions.map(async (session, sessionIndex) => {
    const sessionNumber = sessionIndex + 1;
    let sentLogs = 0;
    let tokens = 0;
    let lastRefill = Date.now();

    while (!stopRequested && Date.now() < stopAt) {
      if (options.transport === 'websocket' && currentState !== 'connected') {
        if (disconnectedAt > 0 && Date.now() - disconnectedAt > options.timeoutSeconds * 1000) {
          throw new Error(`WebSocket stayed disconnected longer than ${options.timeoutSeconds}s.`);
        }

        await sleep(100);
        continue;
      }

      if (options.messagesPerSecond > 0) {
        const now = Date.now();
        const elapsedSeconds = (now - lastRefill) / 1000;
        if (elapsedSeconds > 0) {
          tokens = Math.min(options.messagesPerSecond, tokens + elapsedSeconds * options.messagesPerSecond);
          lastRefill = now;
        }

        if (tokens < 1) {
          await sleep(1);
          continue;
        }

        tokens -= 1;
      }

      sentLogs += 1;
      const logMethod = pickLogMethod(session, sentLogs);
      logMethod(`[${String(sessionNumber).padStart(2, '0')}] Event ${sentLogs}`, payload);
      stats.logsSent += 1;

      if (options.watchesEvery > 0 && sentLogs % options.watchesEvery === 0) {
        session.watchInt(`${session.name}.Rate`, sentLogs);
        stats.watchesSent += 1;
      }

      if (options.flowsEvery > 0 && sentLogs % options.flowsEvery === 0) {
        if (sentLogs % (options.flowsEvery * 2) === 0) {
          session.leaveMethod(`Worker${sessionNumber}.Tick${sentLogs}`);
        } else {
          session.enterMethod(`Worker${sessionNumber}.Tick${sentLogs}`);
        }

        stats.flowsSent += 1;
      }
    }
  });

  try {
    await Promise.all(workers);
  } finally {
    stopRequested = true;
    clearInterval(reporter);
    smartInspect.disconnect();
  }

  if (options.transport === 'websocket' && !connectedAtLeastOnce) {
    throw new Error('WebSocket transport never established a successful connection.');
  }

  console.log(
    `${new Date().toISOString()} | completed transport=${options.transport} | ` +
    `logs=${stats.logsSent} | watches=${stats.watchesSent} | flows=${stats.flowsSent} | errors=${stats.errors}`);
}

main().catch(error => {
  console.error(`${new Date().toISOString()} | fatal=${error.stack ?? error.message}`);
  process.exitCode = 1;
});
