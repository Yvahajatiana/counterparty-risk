import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, Injector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { summaryResource } from './dashboard-api';

describe('dashboard resources', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('exposes undefined instead of throwing when the request fails', async () => {
    const data = runInInjectionContext(TestBed.inject(Injector), () => summaryResource());
    TestBed.inject(ApplicationRef).tick();

    TestBed.inject(HttpTestingController)
      .expectOne('/api/dashboard/summary')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(data.error()).toBeTruthy();
    expect(() => data.value()).not.toThrow();
    expect(data.value()).toBeUndefined();
  });
});
