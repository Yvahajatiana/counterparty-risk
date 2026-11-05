import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

const STORAGE_KEY = 'cr.ui.theme';

describe('ThemeService', () => {
  let root: HTMLElement;
  let storage: Storage;

  function createService(): ThemeService {
    const service = TestBed.inject(ThemeService);
    TestBed.tick();
    return service;
  }

  beforeEach(() => {
    const document = TestBed.inject(DOCUMENT);
    root = document.documentElement;
    storage = document.defaultView!.localStorage;
    storage.clear();
    root.removeAttribute('data-theme');
  });

  afterEach(() => {
    storage.clear();
    root.removeAttribute('data-theme');
  });

  it('follows the system preference by default', () => {
    const service = createService();

    expect(service.mode()).toBe('system');
    expect(root.hasAttribute('data-theme')).toBe(false);
  });

  it('cycles system -> light -> dark -> system and reflects it on <html>', () => {
    const service = createService();

    service.cycle();
    TestBed.tick();
    expect(root.getAttribute('data-theme')).toBe('light');

    service.cycle();
    TestBed.tick();
    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(storage.getItem(STORAGE_KEY)).toBe('dark');

    service.cycle();
    TestBed.tick();
    expect(root.hasAttribute('data-theme')).toBe(false);
    expect(storage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('restores a stored preference and ignores unexpected values', () => {
    storage.setItem(STORAGE_KEY, 'dark');
    expect(createService().mode()).toBe('dark');
  });

  it('applies a stored preference synchronously, before any change detection', () => {
    storage.setItem(STORAGE_KEY, 'dark');

    TestBed.inject(ThemeService);

    expect(root.getAttribute('data-theme')).toBe('dark');
  });

  it('falls back to system on an invalid stored value', () => {
    storage.setItem(STORAGE_KEY, '<script>');
    expect(createService().mode()).toBe('system');
  });
});
