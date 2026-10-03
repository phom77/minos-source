import { convertToParamMap } from '@angular/router';
import { readCatalogQuery } from './catalog-query';

describe('catalog URL parsing', () => {
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
});
