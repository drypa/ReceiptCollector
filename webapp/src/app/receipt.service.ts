import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Receipt } from './receipt';

@Injectable({
  providedIn: 'root'
})
export class ReceiptService {

  constructor(private http: HttpClient) { }


  getReceipts(): Observable<Receipt[]> {
    return this.http.get<Receipt[]>('/api/receipt');
  }

  get(id: string): Observable<Receipt> {
    return this.http.get<Receipt>(`/api/receipt/${id}`);
  }

  addReceiptByBarCode(parsedBarCode: string): Observable<void> {
    return this.http.post<void>('/api/receipt/from-bar-code?' + parsedBarCode, null);
  }
}
