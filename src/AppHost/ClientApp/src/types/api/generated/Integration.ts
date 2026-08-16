/* eslint-disable */
/* tslint:disable */
// @ts-nocheck
/*
 * ---------------------------------------------------------------
 * ## THIS FILE WAS GENERATED VIA SWAGGER-TYPESCRIPT-API        ##
 * ##                                                           ##
 * ## AUTHOR: acacode                                           ##
 * ## SOURCE: https://github.com/acacode/swagger-typescript-api ##
 * ---------------------------------------------------------------
 */

import type { RequestParams } from "./http-client";
import { ContentType } from "./http-client";

import type {
  BaseResultDTO,
  ConfigureRadarrIntegrationRequest,
  ConfigureSonarrIntegrationRequest,
  DiscoverPerformanceStatsDTO,
  GetDiscoverMediaIdentitiesRequest,
  GetDiscoverMediaIdentitiesResponse,
  GetDiscoverMediaSnapshotRequest,
  GetDiscoverMediaSnapshotResponse,
  GetDiscoverTvEpisodePlanRequest,
  GetDiscoverTvEpisodePlanResponse,
  GetDiscoverWantedEndpointResponse,
  LibraryReconciliationActionDTO,
  LibraryReconciliationSettingsDTO,
  LibraryReconciliationStatusDTO,
  MoveConcurrencyStatusDTO,
  PlexLibraryRefreshRequest,
  TestConnectionToRadarrEndpointResponse,
  TestConnectionToSonarrEndpointResponse,
  TmdbIntegrationSettingsRequest,
  TmdbIntegrationStatusDTO,
  TmdbIntegrationTestResponseDTO,
  UpdateMoveConcurrencySettingsRequest,
} from "./data-contracts";

import { apiCheckPipe, axiosObservable } from "@api/base";
import queryString from "query-string";

export class Integration {
  /**
   * No description
   * * @tags Integration
   * @name GetDiscoverPerformanceStatsEndpoint
   * @request GET:/api/Integration/Discover/Performance
   * @secure
   */
  getDiscoverPerformanceStatsEndpoint = (params: RequestParams = {}) =>
    axiosObservable<DiscoverPerformanceStatsDTO>({
      url: `/api/Integration/Discover/Performance`,
      method: "GET",
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<DiscoverPerformanceStatsDTO>);

  /**
   * No description
   * * @tags Integration
   * @name ClearDiscoverPerformanceCacheEndpoint
   * @request DELETE:/api/Integration/Discover/Performance/Cache
   * @secure
   */
  clearDiscoverPerformanceCacheEndpoint = (
    query: {
      scope: string;
    },
    params: RequestParams = {},
  ) =>
    axiosObservable<DiscoverPerformanceStatsDTO>({
      url: `/api/Integration/Discover/Performance/Cache`,
      method: "DELETE",
      params: query,
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<DiscoverPerformanceStatsDTO>);

  /**
   * No description
   * * @tags Integration
   * @name GetDiscoverMediaIdentitiesEndpoint
   * @request POST:/api/Integration/Discover/Identity
   * @secure
   */
  getDiscoverMediaIdentitiesEndpoint = (
    data: GetDiscoverMediaIdentitiesRequest,
    params: RequestParams = {},
  ) =>
    axiosObservable<GetDiscoverMediaIdentitiesResponse>({
      url: `/api/Integration/Discover/Identity`,
      method: "POST",
      data: data,
      secure: true,
      type: ContentType.Json,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<GetDiscoverMediaIdentitiesResponse>);

  /**
   * No description
   * * @tags Integration
   * @name GetDiscoverMediaSnapshotEndpoint
   * @request POST:/api/Integration/Discover/MediaSnapshot
   * @secure
   */
  getDiscoverMediaSnapshotEndpoint = (
    data: GetDiscoverMediaSnapshotRequest,
    params: RequestParams = {},
  ) =>
    axiosObservable<GetDiscoverMediaSnapshotResponse>({
      url: `/api/Integration/Discover/MediaSnapshot`,
      method: "POST",
      data: data,
      secure: true,
      type: ContentType.Json,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<GetDiscoverMediaSnapshotResponse>);

  /**
   * No description
   * * @tags Integration
   * @name GetDiscoverTvEpisodePlanEndpoint
   * @request POST:/api/Integration/Discover/TvEpisodePlan
   * @secure
   */
  getDiscoverTvEpisodePlanEndpoint = (
    data: GetDiscoverTvEpisodePlanRequest,
    params: RequestParams = {},
  ) =>
    axiosObservable<GetDiscoverTvEpisodePlanResponse>({
      url: `/api/Integration/Discover/TvEpisodePlan`,
      method: "POST",
      data: data,
      secure: true,
      type: ContentType.Json,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<GetDiscoverTvEpisodePlanResponse>);

  /**
   * No description
   * * @tags Integration
   * @name GetDiscoverWantedEndpoint
   * @request GET:/api/Integration/Discover/Wanted
   * @secure
   */
  getDiscoverWantedEndpoint = (params: RequestParams = {}) =>
    axiosObservable<GetDiscoverWantedEndpointResponse>({
      url: `/api/Integration/Discover/Wanted`,
      method: "GET",
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<GetDiscoverWantedEndpointResponse>);

  /**
   * No description
   * * @tags Integration
   * @name GetLibraryReconciliationSettingsEndpoint
   * @request GET:/api/Integration/LibraryReconciliation
   * @secure
   */
  getLibraryReconciliationSettingsEndpoint = (params: RequestParams = {}) =>
    axiosObservable<LibraryReconciliationStatusDTO>({
      url: `/api/Integration/LibraryReconciliation`,
      method: "GET",
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<LibraryReconciliationStatusDTO>);

  /**
   * No description
   * * @tags Integration
   * @name SaveLibraryReconciliationSettingsEndpoint
   * @request PUT:/api/Integration/LibraryReconciliation
   * @secure
   */
  saveLibraryReconciliationSettingsEndpoint = (
    data: LibraryReconciliationSettingsDTO,
    params: RequestParams = {},
  ) =>
    axiosObservable<LibraryReconciliationStatusDTO>({
      url: `/api/Integration/LibraryReconciliation`,
      method: "PUT",
      data: data,
      secure: true,
      type: ContentType.Json,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<LibraryReconciliationStatusDTO>);

  /**
   * No description
   * * @tags Integration
   * @name RefreshPlexLibraryNowEndpoint
   * @request POST:/api/Integration/LibraryReconciliation/PlexRefresh
   * @secure
   */
  refreshPlexLibraryNowEndpoint = (
    data: PlexLibraryRefreshRequest,
    params: RequestParams = {},
  ) =>
    axiosObservable<LibraryReconciliationActionDTO>({
      url: `/api/Integration/LibraryReconciliation/PlexRefresh`,
      method: "POST",
      data: data,
      secure: true,
      type: ContentType.Json,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<LibraryReconciliationActionDTO>);

  /**
   * No description
   * * @tags Integration
   * @name ClearRadarrConfigurationEndpoint
   * @request DELETE:/api/Integration/Radarr/Configuration
   * @secure
   */
  clearRadarrConfigurationEndpoint = (params: RequestParams = {}) =>
    axiosObservable<BaseResultDTO>({
      url: `/api/Integration/Radarr/Configuration`,
      method: "DELETE",
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<BaseResultDTO>);

  /**
   * No description
   * * @tags Integration
   * @name ConfigureRadarrIntegrationEndpoint
   * @request POST:/api/Integration/Radarr/Configure
   * @secure
   */
  configureRadarrIntegrationEndpoint = (
    data: ConfigureRadarrIntegrationRequest,
    params: RequestParams = {},
  ) =>
    axiosObservable<BaseResultDTO>({
      url: `/api/Integration/Radarr/Configure`,
      method: "POST",
      data: data,
      secure: true,
      type: ContentType.Json,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<BaseResultDTO>);

  /**
   * No description
   * * @tags Integration
   * @name TestConnectionToRadarrEndpoint
   * @request GET:/api/Integration/Radarr/TestConnection
   * @secure
   */
  testConnectionToRadarrEndpoint = (
    query: {
      apiKey: string;
      url: string;
    },
    params: RequestParams = {},
  ) =>
    axiosObservable<TestConnectionToRadarrEndpointResponse>({
      url: `/api/Integration/Radarr/TestConnection`,
      method: "GET",
      params: query,
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<TestConnectionToRadarrEndpointResponse>);

  /**
   * No description
   * * @tags Integration
   * @name ClearSonarrConfigurationEndpoint
   * @request DELETE:/api/Integration/Sonarr/Configuration
   * @secure
   */
  clearSonarrConfigurationEndpoint = (params: RequestParams = {}) =>
    axiosObservable<BaseResultDTO>({
      url: `/api/Integration/Sonarr/Configuration`,
      method: "DELETE",
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<BaseResultDTO>);

  /**
   * No description
   * * @tags Integration
   * @name ConfigureSonarrIntegrationEndpoint
   * @request POST:/api/Integration/Sonarr/Configure
   * @secure
   */
  configureSonarrIntegrationEndpoint = (
    data: ConfigureSonarrIntegrationRequest,
    params: RequestParams = {},
  ) =>
    axiosObservable<BaseResultDTO>({
      url: `/api/Integration/Sonarr/Configure`,
      method: "POST",
      data: data,
      secure: true,
      type: ContentType.Json,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<BaseResultDTO>);

  /**
   * No description
   * * @tags Integration
   * @name TestConnectionToSonarrEndpoint
   * @request GET:/api/Integration/Sonarr/TestConnection
   * @secure
   */
  testConnectionToSonarrEndpoint = (
    query: {
      apiKey: string;
      url: string;
    },
    params: RequestParams = {},
  ) =>
    axiosObservable<TestConnectionToSonarrEndpointResponse>({
      url: `/api/Integration/Sonarr/TestConnection`,
      method: "GET",
      params: query,
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<TestConnectionToSonarrEndpointResponse>);

  /**
   * No description
   * * @tags Integration
   * @name GetTmdbIntegrationEndpoint
   * @request GET:/api/Integration/Tmdb
   * @secure
   */
  getTmdbIntegrationEndpoint = (params: RequestParams = {}) =>
    axiosObservable<TmdbIntegrationStatusDTO>({
      url: `/api/Integration/Tmdb`,
      method: "GET",
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<TmdbIntegrationStatusDTO>);

  /**
   * No description
   * * @tags Integration
   * @name SaveTmdbIntegrationEndpoint
   * @request PUT:/api/Integration/Tmdb
   * @secure
   */
  saveTmdbIntegrationEndpoint = (
    data: TmdbIntegrationSettingsRequest,
    params: RequestParams = {},
  ) =>
    axiosObservable<TmdbIntegrationStatusDTO>({
      url: `/api/Integration/Tmdb`,
      method: "PUT",
      data: data,
      secure: true,
      type: ContentType.Json,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<TmdbIntegrationStatusDTO>);

  /**
   * No description
   * * @tags Integration
   * @name DeleteTmdbIntegrationEndpoint
   * @request DELETE:/api/Integration/Tmdb
   * @secure
   */
  deleteTmdbIntegrationEndpoint = (params: RequestParams = {}) =>
    axiosObservable<TmdbIntegrationStatusDTO>({
      url: `/api/Integration/Tmdb`,
      method: "DELETE",
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<TmdbIntegrationStatusDTO>);

  /**
   * No description
   * * @tags Integration
   * @name TestTmdbIntegrationEndpoint
   * @request POST:/api/Integration/Tmdb/Test
   * @secure
   */
  testTmdbIntegrationEndpoint = (
    data: TmdbIntegrationSettingsRequest,
    params: RequestParams = {},
  ) =>
    axiosObservable<TmdbIntegrationTestResponseDTO>({
      url: `/api/Integration/Tmdb/Test`,
      method: "POST",
      data: data,
      secure: true,
      type: ContentType.Json,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<TmdbIntegrationTestResponseDTO>);

  /**
   * No description
   * * @tags Integration
   * @name ClearTmdbIdentityCacheEndpoint
   * @request DELETE:/api/Integration/Tmdb/IdentityCache
   * @secure
   */
  clearTmdbIdentityCacheEndpoint = (params: RequestParams = {}) =>
    axiosObservable<TmdbIntegrationStatusDTO>({
      url: `/api/Integration/Tmdb/IdentityCache`,
      method: "DELETE",
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<TmdbIntegrationStatusDTO>);

  /**
   * No description
   * * @tags Integration
   * @name GetMoveConcurrencySettingsEndpoint
   * @request GET:/api/Integration/Downloads/MoveConcurrency
   * @secure
   */
  getMoveConcurrencySettingsEndpoint = (params: RequestParams = {}) =>
    axiosObservable<MoveConcurrencyStatusDTO>({
      url: `/api/Integration/Downloads/MoveConcurrency`,
      method: "GET",
      secure: true,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<MoveConcurrencyStatusDTO>);

  /**
   * No description
   * * @tags Integration
   * @name UpdateMoveConcurrencySettingsEndpoint
   * @request PUT:/api/Integration/Downloads/MoveConcurrency
   * @secure
   */
  updateMoveConcurrencySettingsEndpoint = (
    data: UpdateMoveConcurrencySettingsRequest,
    params: RequestParams = {},
  ) =>
    axiosObservable<MoveConcurrencyStatusDTO>({
      url: `/api/Integration/Downloads/MoveConcurrency`,
      method: "PUT",
      data: data,
      secure: true,
      type: ContentType.Json,
      responseType: "json",
      ...params,
    }).pipe(apiCheckPipe<MoveConcurrencyStatusDTO>);
}

export class IntegrationPaths {
  static getDiscoverPerformanceStatsEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Discover/Performance` });

  static clearDiscoverPerformanceCacheEndpoint = (query: { scope: string }) =>
    queryString.stringifyUrl({
      url: `/api/Integration/Discover/Performance/Cache`,
      query,
    });

  static getDiscoverMediaIdentitiesEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Discover/Identity` });

  static getDiscoverMediaSnapshotEndpoint = () =>
    queryString.stringifyUrl({
      url: `/api/Integration/Discover/MediaSnapshot`,
    });

  static getDiscoverTvEpisodePlanEndpoint = () =>
    queryString.stringifyUrl({
      url: `/api/Integration/Discover/TvEpisodePlan`,
    });

  static getDiscoverWantedEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Discover/Wanted` });

  static getLibraryReconciliationSettingsEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/LibraryReconciliation` });

  static saveLibraryReconciliationSettingsEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/LibraryReconciliation` });

  static refreshPlexLibraryNowEndpoint = () =>
    queryString.stringifyUrl({
      url: `/api/Integration/LibraryReconciliation/PlexRefresh`,
    });

  static clearRadarrConfigurationEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Radarr/Configuration` });

  static configureRadarrIntegrationEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Radarr/Configure` });

  static testConnectionToRadarrEndpoint = (query: {
    apiKey: string;
    url: string;
  }) =>
    queryString.stringifyUrl({
      url: `/api/Integration/Radarr/TestConnection`,
      query,
    });

  static clearSonarrConfigurationEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Sonarr/Configuration` });

  static configureSonarrIntegrationEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Sonarr/Configure` });

  static testConnectionToSonarrEndpoint = (query: {
    apiKey: string;
    url: string;
  }) =>
    queryString.stringifyUrl({
      url: `/api/Integration/Sonarr/TestConnection`,
      query,
    });

  static getTmdbIntegrationEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Tmdb` });

  static saveTmdbIntegrationEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Tmdb` });

  static deleteTmdbIntegrationEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Tmdb` });

  static testTmdbIntegrationEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Tmdb/Test` });

  static clearTmdbIdentityCacheEndpoint = () =>
    queryString.stringifyUrl({ url: `/api/Integration/Tmdb/IdentityCache` });

  static getMoveConcurrencySettingsEndpoint = () =>
    queryString.stringifyUrl({
      url: `/api/Integration/Downloads/MoveConcurrency`,
    });

  static updateMoveConcurrencySettingsEndpoint = () =>
    queryString.stringifyUrl({
      url: `/api/Integration/Downloads/MoveConcurrency`,
    });
}
