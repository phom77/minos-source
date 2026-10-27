import { convertToParamMap } from '@angular/router';
import { readCatalogQuery } from './catalog-query';

describe('catalog URL parsing', () => {
  it('preserves supported group/exclusion filters and rejects orphaned operators', () => {
    expect(readCatalogQuery(convertToParamMap({ difficulty: 'Hard', topic: 'math', match: 'Any', difficultyIsNot: 'true' })))
      .toEqual({ page: 1, pageSize: 20, difficulty: 'Hard', topic: 'math', match: 'Any', difficultyIsNot: 'true' });
    for (const params of [{ difficultyIsNot: 'true' }, { statusIsNot: 'false' }, { match: 'Unknown' }, { topic: 'math', topicIsNot: ['true', 'true'] }])
      expect(readCatalogQuery(convertToParamMap(params))).toBeNull();
  });
  it('defaults absent fields and accepts supported integer values', () => {
    expect(readCatalogQuery(convertToParamMap({}))).toEqual({ page: 1, pageSize: 20 });
    expect(readCatalogQuery(convertToParamMap({ page: '2', pageSize: '100', auth: 'login' }))).toEqual({ page: 2, pageSize: 100 });
    expect(readCatalogQuery(convertToParamMap({ pageSize: '7' }))).toEqual({ page: 1, pageSize: 7 });
  });
  it.each(['0', '-1', '1.5', '1e2', '0x10', '', '01', 'NaN', '9007199254740992', '2147483648'])('rejects invalid page %s', page => {
    expect(readCatalogQuery(convertToParamMap({ page }))).toBeNull();
  });
  it('rejects duplicate fields, excessive sizes and overflowing backend offsets', () => {
    expect(readCatalogQuery(convertToParamMap({ page: ['1', '2'] }))).toBeNull();
    expect(readCatalogQuery(convertToParamMap({ pageSize: '101' }))).toBeNull();
    expect(readCatalogQuery(convertToParamMap({ page: '2147483647', pageSize: '20' }))).toBeNull();
  });
  it('preserves supported library filters and ignores authentication overlay parameters', () => {
    expect(readCatalogQuery(convertToParamMap({ q: '  #42 ', difficulty: 'Hard', topic: 'dynamic-programming', status: 'Solved', sort: 'Newest', auth: 'login' })))
      .toEqual({ page: 1, pageSize: 20, q: '#42', difficulty: 'Hard', topic: 'dynamic-programming', status: 'Solved', sort: 'Newest' });
  });
  it.each([{ q: 'a'.repeat(121) }, { q: '\n' }, { difficulty: 'hard' }, { topic: 'Math' }, { topic: 'math,dp' },
    { status: 'Accepted' }, { sort: 'random' }, { status: ['Solved', 'Attempted'] }])('rejects unsupported or ambiguous filters', params => {
    expect(readCatalogQuery(convertToParamMap(params))).toBeNull();
  });
});
