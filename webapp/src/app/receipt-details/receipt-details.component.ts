import {Component, OnDestroy, OnInit} from '@angular/core';
import {ActivatedRoute} from "@angular/router";
import {ReceiptService} from "../receipt.service";
import {Subject} from "rxjs";
import {first, flatMap, map, takeUntil, tap} from "rxjs/operators";
import {Receipt, RequestStatus} from "../receipt";
import {MatDialog} from "@angular/material/dialog";
import {RequestResultComponent} from "../request-result/request-result.component";

@Component({
  selector: 'app-receipt-details',
  templateUrl: './receipt-details.component.html',
  styleUrls: ['./receipt-details.component.scss']
})
export class ReceiptDetailsComponent implements OnInit, OnDestroy {
  receipt: Receipt;
  private destroy$ = new Subject<boolean>();

  constructor(
    private route: ActivatedRoute,
    private receiptService: ReceiptService,
    private dialog: MatDialog) {
  }

  ngOnInit() {
    this.loadReceipt();
  }

  loadReceipt() {
    this.route.paramMap.pipe(
      first(),
      map(p => p.get('id')),
      flatMap(id => this.receiptService.get(id)),
      tap(receipt => this.receipt = receipt),
      takeUntil(this.destroy$)
    ).subscribe();
  }

  ngOnDestroy(): void {
    this.destroy$.next(true);
    this.destroy$.complete();
  }

  sendOdfsRequest(): void {
    this.receiptService.odfsRequest(this.receipt.id)
      .pipe(
        first(),
        takeUntil(this.destroy$)
      ).subscribe(x => this.openDialog(x));
  }

  sendKktsRequest(): void {
    this.receiptService.kktsRequest(this.receipt.id)
      .pipe(
        first(),
        takeUntil(this.destroy$)
      ).subscribe(x => {
      this.loadReceipt();
      this.openDialog(x);
    });
  }

  needShowKktsStatus(receipt: Receipt): boolean {
    return receipt.kktsRequestStatus && receipt.kktsRequestStatus != RequestStatus.success;
  }

  openDialog(message: string): void {
    this.dialog.open(RequestResultComponent, {
      width: '250px',
      data: message
    })
  }
}
