import { record } from './submission.models';

export interface PerformanceMetric {
  readonly value: number; readonly beatsPercentage: number | null; readonly equalCount: number | null;
  readonly rangeMaximum: number; readonly buckets: readonly { readonly index: number; readonly count: number }[];
}
export interface SubmissionPerformance {
  readonly submissionId: string;
  readonly status: 'Ready' | 'InsufficientSamples' | 'Unprofiled' | 'NotAccepted';
  readonly sampleCount: number; readonly minimumSampleCount: number;
  readonly runtime: PerformanceMetric | null; readonly memory: PerformanceMetric | null;
}
const integer = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
export function isSubmissionPerformance(value: unknown, id: string): value is SubmissionPerformance {
  if (!record(value) || typeof value['submissionId'] !== 'string' || value['submissionId'].toLowerCase() !== id.toLowerCase() ||
      !['Ready', 'InsufficientSamples', 'Unprofiled', 'NotAccepted'].includes(String(value['status'])) ||
      !integer(value['sampleCount']) || !integer(value['minimumSampleCount']) || value['minimumSampleCount'] < 1) return false;
  const ready = value['status'] === 'Ready', samples = value['sampleCount'];
  if (ready && samples < value['minimumSampleCount']) return false;
  if (value['status'] === 'InsufficientSamples' && samples >= value['minimumSampleCount']) return false;
  return ['runtime', 'memory'].every(key => {
    const metric = value[key];
    if (metric === null) return !ready && value['status'] !== 'InsufficientSamples';
    if (!record(metric) || !integer(metric['value']) || !Array.isArray(metric['buckets'])) return false;
    if (!ready) return metric['beatsPercentage'] === null && metric['equalCount'] === null && metric['buckets'].length === 0;
    return typeof metric['beatsPercentage'] === 'number' && metric['beatsPercentage'] >= 0 && metric['beatsPercentage'] <= 100 &&
      integer(metric['equalCount']) && metric['equalCount'] <= samples && typeof metric['rangeMaximum'] === 'number' &&
      Number.isFinite(metric['rangeMaximum']) && metric['rangeMaximum'] > metric['value'] && metric['buckets'].length === 20 &&
      metric['buckets'].every((bucket, index) => record(bucket) && bucket['index'] === index && integer(bucket['count'])) &&
      metric['buckets'].reduce((sum, bucket) => sum + bucket.count, 0) === samples;
  });
}
