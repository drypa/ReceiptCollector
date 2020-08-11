import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Waste } from './waste';
import { EditWasteRequest } from './contracts/waste/edit-waste-request';

@Injectable({
  providedIn: 'root'
})
export class WasteService {

  constructor(private http: HttpClient) {
  }

  getAll(filter: Filter): Observable<Waste[]> {
    const url = `/api/waste?from=${filter.from}&to=${filter.to}`
    return this.http.get<Waste[]>(url);
  }

  update(id: string, request: EditWasteRequest): Observable<void> {
    const url = `/api/waste/${id}`;
    return this.http.put<void>(url, request);
  }

  get(id: string): Observable<Waste> {
    const url = `/api/waste/${id}`;
    return this.http.get<Waste>(url);
  }
}


export class Filter {
  from: number;
  to: number;
}
