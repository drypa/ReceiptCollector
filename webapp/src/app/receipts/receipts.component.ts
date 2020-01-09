import {Component, OnDestroy, OnInit} from '@angular/core';
import {Receipt, RequestStatus} from '../receipt';
import {ReceiptService} from '../receipt.service';
import {first, takeUntil, tap} from 'rxjs/operators';
import {Subject} from 'rxjs';
import {MatSnackBar} from "@angular/material/snack-bar";

@Component({
  selector: 'app-receipts',
  templateUrl: './receipts.component.html',
  styleUrls: ['./receipts.component.scss']
})
export class ReceiptsComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<boolean>();

  constructor(private receiptService: ReceiptService,
              private snackBar: MatSnackBar) {
  }

  receiptList: Receipt[];

  ngOnInit() {
    this.loadData();
  }

  delete(id: string) {
    this.receiptService.delete(id)
      .pipe(
        first(),
        takeUntil(this.destroy$))
      .subscribe(() => {
        this.showSnack("Deleted");
        this.loadData();
      }, err => this.showSnack("Error"));
  }

  ngOnDestroy(): void {
    this.destroy$.next(true);
    this.destroy$.complete();
  }

  private loadData() {
    this.receiptService.getReceipts()
      .pipe(
        first(),
        tap(receipts => this.receiptList = receipts),
        takeUntil(this.destroy$)
      ).subscribe();
  }

  private showSnack(message: string) {
    this.snackBar.open(message, "OK", {})
  }

  isLoaded(receipt: Receipt): boolean {
    return receipt.items && receipt.items.length > 0;
  }

  needShowKktsStatus(receipt: Receipt): boolean {
    return receipt.kktsRequestStatus && receipt.kktsRequestStatus != RequestStatus.success;
  }

}
