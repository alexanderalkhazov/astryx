// A hub connection that never connects: screens under test fall back to polling, and no socket is opened.
// Tests of the realtime client mock '@microsoft/signalr' themselves.
const connection = {
  on: () => undefined,
  onclose: () => undefined,
  onreconnecting: () => undefined,
  onreconnected: () => undefined,
  start: () => new Promise<void>(() => undefined),
  stop: () => Promise.resolve(),
};

export class HubConnectionBuilder {
  withUrl() {
    return this;
  }

  withAutomaticReconnect() {
    return this;
  }

  configureLogging() {
    return this;
  }

  build() {
    return connection;
  }
}

export const LogLevel = { None: 6 };
