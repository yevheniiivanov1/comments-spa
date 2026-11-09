import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

interface GraphQLResponse<T> {
  data?: T;
  errors?: { message: string }[];
}

export interface SearchHit {
  id: number;
  rootId: number | null;
  userName: string;
  email: string;
  createdAt: string;
  /** HTML-escaped text with matches wrapped in <mark> by the server. */
  snippet: string;
}

export interface SearchResult {
  total: number;
  page: number;
  pageSize: number;
  hits: SearchHit[];
}

const SEARCH = `
  query Search($query: String!, $page: Int!) {
    search(query: $query, page: $page) {
      total page pageSize
      hits { id rootId userName email createdAt snippet }
    }
  }`;

/** A minimal GraphQL client: POST /graphql, typed data, errors surfaced as observable errors. */
@Injectable({ providedIn: 'root' })
export class GraphQLClient {
  private readonly http = inject(HttpClient);

  query<T>(query: string, variables: Record<string, unknown> = {}): Observable<T> {
    return this.http.post<GraphQLResponse<T>>('/graphql', { query, variables }).pipe(
      map((response) => {
        if (response.errors?.length || !response.data) {
          throw new Error(response.errors?.map((e) => e.message).join('; ') ?? 'Empty GraphQL response.');
        }
        return response.data;
      }),
    );
  }

  search(query: string, page: number): Observable<SearchResult> {
    return this.query<{ search: SearchResult }>(SEARCH, { query, page }).pipe(map((data) => data.search));
  }
}
