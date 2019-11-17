import {Component, OnDestroy, OnInit} from '@angular/core';
import {Receipt} from '../receipt';
import {ReceiptService} from '../receipt.service';
import {first, takeUntil, tap} from 'rxjs/operators';
import {Subject} from 'rxjs';

@Component({
  selector: 'app-receipts',
  templateUrl: './receipts.component.html',
  styleUrls: ['./receipts.component.scss']
})
export class ReceiptsComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<boolean>();

  constructor(private receiptService: ReceiptService) {
  }

  receiptList: Receipt[];

  ngOnInit() {
    this.receiptService.getReceipts()
      .pipe(
        first(),
        tap(receipts => this.receiptList = receipts),
        takeUntil(this.destroy$)
      ).subscribe();
  }

  ngOnDestroy(): void {
    this.destroy$.next(true);
    this.destroy$.complete();
  }

  delete(id: string) {
    this.receiptService.delete(id)
      .pipe(
        first(),
        takeUntil(this.destroy$))
      .subscribe();
  }
}
