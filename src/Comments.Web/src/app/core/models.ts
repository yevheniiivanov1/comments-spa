export type CommentSort = 'createdAt' | 'userName' | 'email';
export type SortDirection = 'asc' | 'desc';

export interface AttachmentDto {
  kind: 'image' | 'text';
  url: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  width: number | null;
  height: number | null;
}

export interface CommentDto {
  id: number;
  parentId: number | null;
  rootId: number | null;
  depth: number;
  userName: string;
  email: string;
  homePage: string | null;
  /** Server-validated XHTML with only a, code, i and strong. */
  html: string;
  createdAt: string;
  replyCount: number;
  attachment: AttachmentDto | null;
}

export interface CommentPage {
  items: CommentDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  sort: CommentSort;
  direction: SortDirection;
}

export interface ThreadPage {
  rootId: number;
  items: CommentDto[];
  nextAfter: number | null;
}

export interface Captcha {
  id: string;
  image: string;
  answer?: string;
}

export interface PreviewResult {
  html: string | null;
  errors: string[];
}

export interface NewComment {
  userName: string;
  email: string;
  homePage: string;
  text: string;
  parentId: number | null;
  captchaId: string;
  captchaAnswer: string;
  file: File | null;
}

/** ASP.NET Core validation problem details: errors keyed by form field. */
export interface ValidationProblem {
  status: number;
  title?: string;
  errors?: Record<string, string[]>;
}
