import { Component, OnInit } from '@angular/core';
import { Observable } from 'rxjs';
import { Receipt } from "../receipt";
import { ReceiptService } from "../receipt.service";
import { tap } from 'rxjs/operators';

@Component({
  selector: 'app-receipts',
  templateUrl: './receipts.component.html',
  styleUrls: ['./receipts.component.scss']
})
export class ReceiptsComponent implements OnInit {

  constructor(private receiptService: ReceiptService) {
  }

  receiptList: Receipt[];

  ngOnInit() {
    this.receiptService.getReceipts()
      .pipe(tap(
        receipts => this.receiptList = receipts
      ));
  }


}
