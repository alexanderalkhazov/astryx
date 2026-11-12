/** Query keys for AI analyses, shared by the job poller and the realtime handler. */
export const analysisKeys = {
  all: ['analysis'] as const,
  job: (jobId: string) => ['analysis', 'job', jobId] as const,
  result: (resultId: string) => ['analysis', 'result', resultId] as const,
  list: (symbol: string) => ['analysis', 'list', symbol] as const,
};
